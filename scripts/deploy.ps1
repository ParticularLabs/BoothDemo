param(
    [Parameter(Mandatory = $true)]
    [string] $ResourceGroupName,

    [string] $Location = "westeurope",

    [string] $GitHubOwner = "particularlabs",

    [string] $GitHubRepo = "boothdemo",

    [string] $BillingQueueName = "billing",
    [string] $ShippingQueueName = "shipping",

    [int] $MinReplicas = 0,
    [int] $MaxReplicas = 5,
    [int] $MessageCount = 25
)

$ErrorActionPreference = "Stop"

$environmentName = "$ResourceGroupName-containerapps-env"
$serviceBusNamespace = "$ResourceGroupName-servicebus"
$registry = "ghcr.io"
$imagePrefix = "$registry/$GitHubOwner/$GitHubRepo"

Write-Host "Creating resource group..."
az group create `
  --name $ResourceGroupName `
  --location $Location `
  --output none

Write-Host "Creating Container Apps environment..."
az containerapp env create `
  --name $environmentName `
  --resource-group $ResourceGroupName `
  --location $Location `
  --output none

# -----------------------------
# Service Bus setup
# -----------------------------

Write-Host "Creating Service Bus namespace..."
az servicebus namespace create `
  --name $serviceBusNamespace `
  --resource-group $ResourceGroupName `
  --location $Location `
  --sku Standard `
  --output none

Write-Host "Retrieving Service Bus connection string..."
$ServiceBusConnectionString = az servicebus namespace authorization-rule keys list `
  --resource-group $ResourceGroupName `
  --namespace-name $serviceBusNamespace `
  --name RootManageSharedAccessKey `
  --query primaryConnectionString `
  --output tsv

# -----------------------------
# Container Apps deployment
# -----------------------------

function Deploy-ContainerApp {
    param(
        [string] $AppName,
        [string] $ImageName,
        [string] $QueueName
    )

    Write-Host "Deploying $AppName..."

    az containerapp create `
      --name $AppName `
      --resource-group $ResourceGroupName `
      --environment $environmentName `
      --image "$imagePrefix/$ImageName`:latest" `
      --min-replicas $MinReplicas `
      --max-replicas $MaxReplicas `
      --secrets "servicebus-connection-string=$ServiceBusConnectionString" `
      --env-vars "AzureServiceBus_ConnectionString=secretref:servicebus-connection-string" `
      --scale-rule-name "$AppName-servicebus-scale" `
      --scale-rule-type azure-servicebus `
      --scale-rule-metadata "queueName=$QueueName" "messageCount=$MessageCount" `
      --scale-rule-auth "connection=servicebus-connection-string" `
      --output none
}

Deploy-ContainerApp `
  -AppName "billing" `
  -ImageName "billing" `
  -QueueName $BillingQueueName

Deploy-ContainerApp `
  -AppName "shipping" `
  -ImageName "shipping" `
  -QueueName $ShippingQueueName

Write-Host "Deployment complete."