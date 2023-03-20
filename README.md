# Microsoft.Diagnostics.EventFlow.Outputs.Dynatrace

## Introduction
The **EventFlow library suite** allows applications to define what diagnostics data to collect, and where they should be outputted to.Diagnostics data can be anything from performance counters to application traces.

More information can be found here:  [Microsoft.Diagnostics.EventFlow](https://github.com/Azure/diagnostics-eventflow/)


#### Dynatrace 

The Dynatrace output writes data to [Dynatrace](https://www.dynatrace.com) tenants. You will need to create a Dynatrace account and know its tenant and api-token before using Dynatrace output. Here is a sample configuration fragment enabling the output:
```json
{

    "type": "Dynatrace",
    "APIToken": "<YOUR API TOKEN>",
    "ServiceBaseAddress": "https://<YOUR-TENANT-ID>.live.dynatrace.com",
    "ServiceAPIEndpoint": "/api/",
    "MonitoredEntity": {
        "entityAlias": "ServiceFabric Cluster",
        "displayName": "ServiceFabric Cluster",
        "type": "ServiceFabric",
        "ipAddresses": [ "azure-metadata" ],
        "listenPorts": [ "8080" ],
        "tags": [ "ServiceFabric" ],
        "configUrl": "http://localhost:19080",
        "favicon": "https://assets.dynatrace.com/global/icons/white/icons_technologies_003_microsoft-azure-fabric.png",
        "Timeseries": {
            "timeseriesID": "servicefabric.events",
            "displayName": "ServiceFabric Events",
            "unit": "Count",
            "dimensions": [ "channel", "event" ],
            "types": [ "ServiceFabric" ]
        }
    } 
    
}
```

Supported configuration settings are:

| Field | Values/Types | Required | Description |
| :---- | :-------------- | :------: | :---------- |
| `type` | "Dynatrace" | Yes | Specifies the output type. For this output, it must be "Dynatrace". |
| `APIToken` | string (GUID) | Yes | Specifies the Dynatrace API Token |
| `ServiceBaseAddress` | string | Yes | Specifies the API base address for your tenant |
| `entityAlias` | string | Yes | Either a custom name or in case of Azure Virtual Machine "azure-metadata" to capture the name from the Azure VM |
| `ipAddresses` | string | Yes | Either the entities ip-adress(es) or in case of Azure Virtual Machine "azure-metadata" to retrieve the ip-addresses from the Azure VM |
| `listenPorts` | string | Yes | Ports used by the servicefabric services |
| `configUrl` | string | true | Should be the endpoint of ServiceFabric Explorer, so Dynatrace automatically links from Dynatrace UI into SF explorer |
| `Timeseries` | object | false | If defined, event statistics are tracked as a custom timeseries based on "ProviderName" and "Event-ID" |

For more information about how to use Metrics in Dynatrace see [**Dynatrace Device Metrics**](https://www.dynatrace.com/support/help/dynatrace-api/environment/timeseries-api/manage-custom-metrics-via-api/)


*Metadata support*

Dynatrace output supports the standard "metric" metadata. 
Additionally the "dynatrace-event" metadata type is available to customize how the event is sent to dynatrace. 

| Field | Values/Types | Required | Description |
| :---- | :-------------- | :------: | :---------- |
| `metadata` | "dynatrace-event" | false | Indicates Dynatrace event metadata; must be "dynatrace-event". |
| `source` | string | false | Overrides the "source" property of the event (default is "eventflow") |
| `eventType` | string | false | Overrides the "eventType" property of the event (default is "CUSTOM_ANNOTATION") |
| `annotationType` | string | false | Overrides the "annotationType" property of the event (default is "{eventName} - {eventID}") |
| `annotationTypeProperty` | string | false | Maps an incoming event-property to "annotationType" property  |
| `annotationDescription` | string | false | Overrides the "annotationDescription" property of the event (default is "{eventMessage}") |
| `annotationDescriptionProperty` | string | false | Maps an incoming event-property to "annotationDescription" property |
| `description` | string | false | Sets the "description" property of the incoming event |
| `descriptionProperty` | string | false | Maps an incoming event-property to "description" property  |
| `deploymentName` | string | false | Sets the "deploymentName" property of the event
| `deploymentNameProperty` | string | false | Maps an incoming event-property to "deploymentName" property
| `deploymentVersion` | string | false | Sets the "deploymentVersion" property of the event
| `deploymentVersionProperty` | string | false | Maps an incoming event-property to "deploymentVersion" property
| `configuration` | string | false | Sets the "configuration" property of the event
| `configurationProperty` | string | false | Maps an incoming event-property to "configuration" property
| `tagMatchEntityType` | string | false | filters matching entities by entity-type e.g. "HOST" [read more](https://www.dynatrace.com/support/help/dynatrace-api/environment/events-api/#events-post-parameter-taginfo)
| `tagMatchContext` | string | true | Filters matching tags by context. e.g. "CONTEXTLESS" matching entities by entity-type e.g. "HOST" [read more](https://www.dynatrace.com/support/help/dynatrace-api/environment/events-api/#events-post-parameter-enum-gWW16rUCfKLr7Ud9I1FpMA)
| `tagMatchKey` | string | false | Sets the matching tag-key 
| `tagMatchValue` | string | true | Sets the matching tag-value 
| `tagMatchValueProperty` | string | true | Maps an incoming event-property to match a tag-value 



For more information about how to use the event properties see [**Dynatrace Event API**](https://www.dynatrace.com/support/help/shortlink/api-events#events-post-parameter-eventpushmessage)



## Platform Support
EventFlow supports full .NET Framework (.NET 4.5 series and 4.6 series) and .NET Core, but not all inputs and outputs are supported on all platforms. 
The following table lists platform support for standard inputs and outputs.  

| Name | .NET 4.5.1 | .NET 4.6 | .NET Core |
| :------------ | :---- | :---- | :---- |
| *Outputs* |
| [Dynatrace](#dynatrace) | Yes | Yes | Yes |

