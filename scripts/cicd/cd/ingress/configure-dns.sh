#!/bin/bash

# Public IP address of your ingress controller
IP="40.117.62.39"

# Name to associate with public IP address
DNSNAME="rmcd-staging"

# Get the resource-id of the public ip
PUBLICIPID=$(az network public-ip list --query "[?ipAddress!=null]|[?contains(ipAddress, '$IP')].[id]" --output tsv)

# Update public ip address with DNS name
az network public-ip update --ids $PUBLICIPID --dns-name $DNSNAME
