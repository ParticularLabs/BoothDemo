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
    [int] $MessageCount = 25,

    # Particular Service Platform
    [string] $PlatformVersion = "latest",
    [string] $ServicePulseVersion = "latest",
    [string] $ParticularLicense = "",

    # Telemetry: public URL of the booth's OpenTelemetry Collector (the tunnel to its token-protected OTLP/HTTP receiver,
    # port 4319 on the main Pi) and the token it expects. When empty, the apps don't export telemetry.
    [string] $OtlpEndpoint = "",
    [string] $OtlpToken = "",

    [switch] $SkipServices,
    [switch] $SkipPlatform
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

az containerapp env show --name $environmentName --resource-group $ResourceGroupName --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating Container Apps environment..."
    az containerapp env create `
      --name $environmentName `
      --resource-group $ResourceGroupName `
      --location $Location `
      --output none
}
else {
    Write-Host "Container Apps environment already exists."
}

# -----------------------------
# Service Bus setup
# -----------------------------

az servicebus namespace show --name $serviceBusNamespace --resource-group $ResourceGroupName --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating Service Bus namespace..."
    az servicebus namespace create `
      --name $serviceBusNamespace `
      --resource-group $ResourceGroupName `
      --location $Location `
      --sku Standard `
      --output none
}
else {
    Write-Host "Service Bus namespace already exists."
}

Write-Host "Retrieving Service Bus connection string..."
$ServiceBusConnectionString = az servicebus namespace authorization-rule keys list `
  --resource-group $ResourceGroupName `
  --namespace-name $serviceBusNamespace `
  --name RootManageSharedAccessKey `
  --query primaryConnectionString `
  --output tsv

# -----------------------------
# Telemetry
# -----------------------------
# Standard OpenTelemetry exporter variables, honored by the endpoints (via OpenTelemetryUtils) and by ServiceControl.
# The bearer token goes in a secret because it is part of OTEL_EXPORTER_OTLP_HEADERS.

$otlpHeaders = if ($OtlpToken) { "Authorization=Bearer $OtlpToken" } else { "none" }

function Get-TelemetryEnv {
    if (-not $OtlpEndpoint) {
        return @()
    }
    @(
        @{ name = "OTEL_EXPORTER_OTLP_ENDPOINT"; value = $OtlpEndpoint }
        @{ name = "OTEL_EXPORTER_OTLP_PROTOCOL"; value = "http/protobuf" }
        @{ name = "OTEL_EXPORTER_OTLP_HEADERS"; secretRef = "otlp-headers" }
        # The OpenTelemetry default is 60 seconds, too slow for live graphs at the booth
        @{ name = "OTEL_METRIC_EXPORT_INTERVAL"; value = "5000" }
    )
}

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

    $secrets = @("servicebus-connection-string=$ServiceBusConnectionString", "otlp-headers=$otlpHeaders")
    $envVars = @("AzureServiceBus_ConnectionString=secretref:servicebus-connection-string") +
        @(Get-TelemetryEnv | ForEach-Object { if ($_.secretRef) { "$($_.name)=secretref:$($_.secretRef)" } else { "$($_.name)=$($_.value)" } })

    az containerapp create `
      --name $AppName `
      --resource-group $ResourceGroupName `
      --environment $environmentName `
      --image "$imagePrefix/$ImageName`:latest" `
      --min-replicas $MinReplicas `
      --max-replicas $MaxReplicas `
      --secrets $secrets `
      --env-vars $envVars `
      --scale-rule-name "$AppName-servicebus-scale" `
      --scale-rule-type azure-servicebus `
      --scale-rule-metadata "queueName=$QueueName" "messageCount=$MessageCount" `
      --scale-rule-auth "connection=servicebus-connection-string" `
      --output none
}

if (-not $SkipServices) {
    Deploy-ContainerApp `
      -AppName "billing" `
      -ImageName "billing" `
      -QueueName $BillingQueueName

    Deploy-ContainerApp `
      -AppName "shipping" `
      -ImageName "shipping" `
      -QueueName $ShippingQueueName
}

# -----------------------------
# Particular Service Platform
# -----------------------------
# ServiceControl (error), ServiceControl Audit, Monitoring and ServicePulse, all using Azure Service Bus.
# Apps reach each other inside the environment via http://<app-name> (internal ingress, port 80).
# Each ServiceControl instance runs its own RavenDB as a sidecar container reachable on localhost.
# NOTE: RavenDB data lives on the container's local disk, so it is lost when the replica restarts.

function Deploy-PlatformApp {
    param(
        [string] $AppName,
        [array] $Containers,
        [int] $TargetPort,
        [bool] $External = $false
    )

    Write-Host "Deploying $AppName..."

    $app = @{
        name       = $AppName
        location   = $Location
        type       = "Microsoft.App/containerApps"
        properties = @{
            managedEnvironmentId = $environmentId
            configuration        = @{
                activeRevisionsMode = "Single"
                secrets             = @(
                    @{ name = "servicebus-connection-string"; value = $ServiceBusConnectionString }
                    @{ name = "particular-license"; value = $(if ($ParticularLicense) { $ParticularLicense } else { "none" }) }
                    @{ name = "otlp-headers"; value = $otlpHeaders }
                )
                ingress             = @{
                    external      = $External
                    targetPort    = $TargetPort
                    transport     = "auto"
                    allowInsecure = -not $External
                }
            }
            template             = @{
                containers = $Containers
                # The platform must always be running to ingest messages, and RavenDB data is per-replica
                scale      = @{ minReplicas = 1; maxReplicas = 1 }
            }
        }
    }

    $yamlFile = New-TemporaryFile
    try {
        # JSON is valid YAML. Written without a byte order mark: Windows PowerShell's "-Encoding utf8" adds one,
        # which the Azure CLI reads as part of the document and then fails to parse it
        [System.IO.File]::WriteAllText($yamlFile.FullName, ($app | ConvertTo-Json -Depth 20))
        az containerapp create `
          --name $AppName `
          --resource-group $ResourceGroupName `
          --yaml $yamlFile `
          --output none
        if ($LASTEXITCODE -ne 0) { throw "Failed to deploy $AppName" }
    }
    finally {
        Remove-Item $yamlFile -Force
    }
}

function New-RavenDbSidecar {
    @{
        name      = "ravendb"
        image     = "particular/servicecontrol-ravendb:$PlatformVersion"
        # Container Apps uses 100.100.x.x addresses, which RavenDB does not consider private, so it refuses
        # unsecured access by default. RavenDB is bound to loopback, so only the ServiceControl container
        # in the same replica can reach it, which makes allowing unsecured access safe.
        env       = @(
            @{ name = "RAVEN_Security_UnsecuredAccessAllowed"; value = "PublicNetwork" }
            @{ name = "RAVEN_ServerUrl"; value = "http://127.0.0.1:8080" }
            @{ name = "RAVEN_PublicServerUrl"; value = "http://127.0.0.1:8080" }
            @{ name = "RAVEN_ServerUrl_Tcp"; value = "tcp://127.0.0.1:38888" }
            @{ name = "RAVEN_PublicServerUrl_Tcp"; value = "tcp://127.0.0.1:38888" }
        )
        resources = @{ cpu = 1.0; memory = "2Gi" }
    }
}

function Get-TransportEnv {
    $envVars = @(
        @{ name = "TRANSPORTTYPE"; value = "NetStandardAzureServiceBus" }
        @{ name = "CONNECTIONSTRING"; secretRef = "servicebus-connection-string" }
    )
    if ($ParticularLicense) {
        $envVars += @{ name = "PARTICULARSOFTWARE_LICENSE"; secretRef = "particular-license" }
    }
    if ($OtlpEndpoint) {
        # ServiceControl and Audit export metrics when OTEL_EXPORTER_OTLP_ENDPOINT is set; all three export logs with Otlp
        $envVars += @{ name = "LOGGINGPROVIDERS"; value = "NLog,Otlp" }
        $envVars += Get-TelemetryEnv
    }
    $envVars
}

if (-not $SkipPlatform) {
    $environmentId = az containerapp env show `
      --name $environmentName `
      --resource-group $ResourceGroupName `
      --query id `
      --output tsv

    Deploy-PlatformApp -AppName "servicecontrol-audit" -TargetPort 44444 -Containers @(
        @{
            name      = "servicecontrol-audit"
            image     = "particular/servicecontrol-audit:$PlatformVersion"
            args      = @("--setup-and-run")
            env       = @(Get-TransportEnv) + @(
                @{ name = "RAVENDB_CONNECTIONSTRING"; value = "http://127.0.0.1:8080" }
            )
            resources = @{ cpu = 0.5; memory = "1Gi" }
        }
        (New-RavenDbSidecar)
    )

    Deploy-PlatformApp -AppName "servicecontrol" -TargetPort 33333 -Containers @(
        @{
            name      = "servicecontrol"
            image     = "particular/servicecontrol:$PlatformVersion"
            args      = @("--setup-and-run")
            env       = @(Get-TransportEnv) + @(
                @{ name = "RAVENDB_CONNECTIONSTRING"; value = "http://127.0.0.1:8080" }
                @{ name = "REMOTEINSTANCES"; value = '[{"api_uri":"http://servicecontrol-audit/api"}]' }
            )
            resources = @{ cpu = 0.5; memory = "1Gi" }
        }
        (New-RavenDbSidecar)
    )

    Deploy-PlatformApp -AppName "servicecontrol-monitoring" -TargetPort 33633 -Containers @(
        @{
            name      = "servicecontrol-monitoring"
            image     = "particular/servicecontrol-monitoring:$PlatformVersion"
            args      = @("--setup-and-run")
            env       = @(Get-TransportEnv)
            resources = @{ cpu = 0.5; memory = "1Gi" }
        }
    )

    # ServicePulse proxies the ServiceControl and Monitoring APIs, so it is the only app exposed externally
    Deploy-PlatformApp -AppName "servicepulse" -TargetPort 9090 -External $true -Containers @(
        @{
            name      = "servicepulse"
            image     = "particular/servicepulse:$ServicePulseVersion"
            env       = @(
                @{ name = "SERVICECONTROL_URL"; value = "http://servicecontrol/" }
                @{ name = "MONITORING_URL"; value = "http://servicecontrol-monitoring/" }
            )
            resources = @{ cpu = 0.25; memory = "0.5Gi" }
        }
    )

    $servicePulseFqdn = az containerapp show `
      --name "servicepulse" `
      --resource-group $ResourceGroupName `
      --query properties.configuration.ingress.fqdn `
      --output tsv

    Write-Host "ServicePulse is available at https://$servicePulseFqdn"
}

Write-Host "Deployment complete."
