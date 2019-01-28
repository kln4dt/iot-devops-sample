namespace SampleModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Blob;

    class Program
    {
        static int counter;
        static ModuleClient ioTHubModuleClient;
        static CloudBlobClient blobClient;


        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Retrieve storage account from connection string.
            var storageAccountName = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_NAME");
            var storageAccountKey = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_KEY");
            var storageConnectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={storageAccountKey}";

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            // Create the blob client.
            Console.WriteLine("Creating blob client");
            blobClient = storageAccount.CreateCloudBlobClient();

            // Open a connection to the Edge runtime
            ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            await ioTHubModuleClient.SetInputMessageHandlerAsync("messagesToUpload", UploadMessage, ioTHubModuleClient);
            
            await ioTHubModuleClient.SetMethodHandlerAsync("HearBeat", HeartBeat, null);
            Console.WriteLine("Set Heartbeat Method Handler:HeartBeat.");

        }

        static async Task<MessageResponse> UploadMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);
            var today = DateTime.UtcNow.Date;

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString) && counterValue%100==0)
            {
                Console.WriteLine("Creating container reference");
                CloudBlobContainer container = blobClient.GetContainerReference("default");
                await container.CreateIfNotExistsAsync();

                Console.WriteLine("Creating block blob reference");
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(today.ToString());

                Console.WriteLine("Uploading block blob");
                await blockBlob.UploadFromByteArrayAsync(messageBytes, 0, messageBytes.Length);

                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                pipeMessage.Properties.Add("blobUri", blockBlob.Uri.ToString());

                await moduleClient.SendEventAsync("output1", pipeMessage);
                Console.WriteLine("Received message sent");
            }
            return MessageResponse.Completed;
        }

        private static Task<MethodResponse> HeartBeat(MethodRequest methodRequest, object userContext) => Task.Run(() =>
        {
            var heartBeatMessage = new Message(Encoding.UTF8.GetBytes(string.Format("Device [{0}], Module [EventHubReader] Running",System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID"))));
            heartBeatMessage.Properties.Add("MessageType", "heartBeat");
            ioTHubModuleClient.SendEventAsync("heartbeat", heartBeatMessage);
            return new MethodResponse(200);

        });
    }
}
