#!/bin/bash

# ACR Module Verification Script
# Requires jq and az cli

usage(){
        echo "***ACR Module Verification Script***"
        echo "Usage: ./acrModuleVerification.sh <acr_url> <module_name1:tag> <module_name2:tag> <module_nameN:tag>"
		echo "Expects at least 2 arguments: acr_url and at least 1 module to verify"
}

verifyModules(){
acr_name=$( echo $acr_url| cut -d'.' -f1 )
for ((i=2;i<=$#;i++))
do
  echo "Verifying ${!i} exists in $acr_name..."
  mod_name=$( echo ${!i}| cut -d':' -f1 )
  mod_tag=$( echo ${!i}| cut -d':' -f2 )
  tag_list=($(az acr repository show-tags --name $acr_name --repository $mod_name| jq -r .[]))
  if [[ " ${tag_list[@]} " =~ " ${mod_tag} " ]]; then
    echo "${!i} exists"
  else
	echo ${!i} does not exist >&2
  fi
done

}

acr_url=$1

#check arguments
[[ $# < 2 ]] && { usage && exit 1; } || verifyModules "$@"