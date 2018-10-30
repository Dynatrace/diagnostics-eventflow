// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Validation;

using Microsoft.Diagnostics.EventFlow.Configuration;
using Microsoft.Diagnostics.EventFlow.Metadata;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Diagnostics.EventFlow.Outputs.Dynatrace.Model;
using System.Text;
using System.Linq;

namespace Microsoft.Diagnostics.EventFlow.Outputs
{
    public class DynatraceOutput : IOutput
    {
        private static readonly Task CompletedTask = Task.FromResult<object>(null);
      
        private DynatraceOutputConfiguration dtOutputConfiguration;
        private CustomDeviceResponse customDeviceID;

        private HttpClient restClient;
        private readonly IHealthReporter healthReporter;

        DateTime? StartWaitMetrics = null;
        DateTime? StartWaitEvents = null;

        string EventEndoint { get { return dtOutputConfiguration.ServiceAPIEndpoint + "v1/events/"; } }
        string TimeSeriesEndoint { get { return dtOutputConfiguration.ServiceAPIEndpoint + "v1/timeseries/"; } }
        string MetricEndoint { get { return dtOutputConfiguration.ServiceAPIEndpoint + "v1/entity/infrastructure/"; } }

        public DynatraceOutput(IConfiguration configuration, IHealthReporter healthReporter)
        {
            Requires.NotNull(configuration, nameof(configuration));
            Requires.NotNull(healthReporter, nameof(healthReporter));

            this.healthReporter = healthReporter;
            dtOutputConfiguration = new DynatraceOutputConfiguration();
            try
            {
                configuration.Bind(dtOutputConfiguration);
            }
            catch
            {
                healthReporter.ReportProblem($"Invalid {nameof(DynatraceOutputConfiguration)} configuration encountered: '{configuration.ToString()}'",
                    EventFlowContextIdentifiers.Configuration);
                throw;
            }

            Initialize(dtOutputConfiguration);
        }

        public DynatraceOutput(DynatraceOutputConfiguration applicationInsightsOutputConfiguration, IHealthReporter healthReporter)
        {
            Requires.NotNull(applicationInsightsOutputConfiguration, nameof(applicationInsightsOutputConfiguration));
            Requires.NotNull(healthReporter, nameof(healthReporter));

            this.healthReporter = healthReporter;

            Initialize(applicationInsightsOutputConfiguration);
        }

        public async Task SendEventsAsync(IReadOnlyCollection<EventData> events, long transmissionSequenceNumber, CancellationToken cancellationToken)
        {
            if (this.restClient == null || events == null || events.Count == 0)
            {
                return;
            }

            try
            {

                MonitoredEntityMetrics m = new MonitoredEntityMetrics()
                {
                    type = dtOutputConfiguration.MonitoredEntity.type
                };

                bool tracked = false;

                foreach (var e in events)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    IReadOnlyCollection<EventMetadata> metadata = null;
                    e.TryGetMetadata(MetricData.MetricMetadataKind, out metadata);

                    if (dtOutputConfiguration.TimeSeries != null)
                        tracked |= TrackMetric(m, e, metadata);

                    e.TryGetMetadata(DynatraceEventData.MetadataKind, out metadata);
                    tracked |= TrackEvent(m, e, metadata);
                }

                try
                {
                    if (m.series.Count > 0)
                    {
                        await SendMetric(dtOutputConfiguration.MonitoredEntity.entityAlias, m);
                    }
                    
                    if (tracked)
                        this.healthReporter.ReportWarning("Event not tracked");
                    else
                        this.healthReporter.ReportHealthy();
                }
                catch (Exception ex)
                {
                    this.healthReporter.ReportProblem("Diagnostics data upload has failed." + Environment.NewLine + ex.ToString());
                    throw;
                }
                
            }
            catch (Exception e)
            {
                this.healthReporter.ReportProblem("Diagnostics data upload has failed." + Environment.NewLine + e.ToString());
                throw;
            }

            return;
        }

        private async void Initialize(DynatraceOutputConfiguration dtOutputConfiguration)
        {
            Debug.Assert(dtOutputConfiguration != null);
            Debug.Assert(this.healthReporter != null);

            
            if (string.IsNullOrWhiteSpace(dtOutputConfiguration.ServiceBaseAddress))
            {
                this.healthReporter.ReportProblem($"{nameof(DynatraceOutput)}: 'ServiceBaseAddress' configuration parameter is not set");
                return;
            }
            if (string.IsNullOrWhiteSpace(dtOutputConfiguration.ServiceAPIEndpoint))
            {
                this.healthReporter.ReportProblem($"{nameof(DynatraceOutput)}: 'ServiceAPIEndpoint' configuration parameter is not set");
                return;
            }
            if (string.IsNullOrWhiteSpace(dtOutputConfiguration.APIToken))
            {
                this.healthReporter.ReportProblem($"{nameof(DynatraceOutput)}: 'APIToken' configuration parameter is not set");
                return;
            }

            if (dtOutputConfiguration.MonitoredEntity == null)
            {
                this.healthReporter.ReportProblem($"{nameof(DynatraceOutput)}: 'MonitoredEntity' configuration is not set");
                return;
            }
          


            restClient = new HttpClient();
            restClient.BaseAddress = new Uri(dtOutputConfiguration.ServiceBaseAddress, UriKind.Absolute);
            restClient.DefaultRequestHeaders.Add("Authorization", "Api-Token "+ dtOutputConfiguration.APIToken);

            InitializeCustomDevice(dtOutputConfiguration);

            InitializeMetrics(dtOutputConfiguration);


        }

        private void InitializeMetrics(DynatraceOutputConfiguration cfg)
        {
            if (cfg.TimeSeries != null)
            {
                InitializeMetric(cfg.TimeSeries.timeseriesId, cfg.TimeSeries as TimeseriesRegistrationMessage);
            }
        }

        private async void InitializeCustomDevice(DynatraceOutputConfiguration cfg)
        {
            bool useIPMeta = dtOutputConfiguration.MonitoredEntity.ipAddresses.Contains("azure-metadata");
            bool useInstanceMeta = dtOutputConfiguration.MonitoredEntity.entityAlias == "azure-metadata";
            if (useIPMeta || useInstanceMeta)
            {
                HttpClient client;
                using (client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Metadata", "true");
                    var response = client.GetAsync("http://169.254.169.254/metadata/instance?api-version=2017-12-01");

                    try
                    {
                        response.Result.EnsureSuccessStatusCode();

                        JObject meta = JObject.Parse(await response.Result.Content.ReadAsStringAsync());

                        if (useInstanceMeta)
                        {
                            dtOutputConfiguration.MonitoredEntity.entityAlias = (string)meta["compute"]["name"];

                            if (dtOutputConfiguration.MonitoredEntity.properties == null)
                                dtOutputConfiguration.MonitoredEntity.properties = new Dictionary<string, string>();
                            dtOutputConfiguration.MonitoredEntity.properties.Add("location", (string)meta["compute"]["location"]);
                            dtOutputConfiguration.MonitoredEntity.properties.Add("osType", (string)meta["compute"]["osType"]);
                            dtOutputConfiguration.MonitoredEntity.properties.Add("resourceGroupName", (string)meta["compute"]["resourceGroupName"]);
                            dtOutputConfiguration.MonitoredEntity.properties.Add("subscriptionId", (string)meta["compute"]["subscriptionId"]);
                            dtOutputConfiguration.MonitoredEntity.properties.Add("vmId", (string)meta["compute"]["vmId"]);
                            if (!string.IsNullOrEmpty((string)meta["compute"]["vmScaleSetName"]))
                                dtOutputConfiguration.MonitoredEntity.properties.Add("vmScaleSetName", (string)meta["compute"]["vmScaleSetName"]);
                            if (!String.IsNullOrEmpty((string)meta["compute"]["zone"]))
                                dtOutputConfiguration.MonitoredEntity.properties.Add("zone", (string)meta["compute"]["zone"]);
                        }
                        if (useIPMeta)
                        {
                                List<string> ip = new List<string>();
                                ip.Add((string)meta["network"]["interface"][0]["ipv4"]["ipAddress"][0]["privateIpAddress"]);
                                if (!String.IsNullOrEmpty((string)meta["network"]["interface"][0]["ipv4"]["ipAddress"][0]["publicIpAddress"]))
                                    ip.Add((string)meta["network"]["interface"][0]["ipv4"]["ipAddress"][0]["publicIpAddress"]);

                                dtOutputConfiguration.MonitoredEntity.ipAddresses = ip.ToArray();
                        }

                    }
                    catch (Exception ex)
                    {

                        this.healthReporter.ReportProblem("Couldn't resolve azure-metadata: " + ex.Message, EventFlowContextIdentifiers.Output);
                        throw;
                    }

                }
            }

            customDeviceID = await CreateCustomDevice(cfg.MonitoredEntity.entityAlias, dtOutputConfiguration.MonitoredEntity);
        }

        private async void InitializeMetric(string metricID, TimeseriesRegistrationMessage metric)
        {

            var httpContent = new StringContent(JsonConvert.SerializeObject(metric), Encoding.UTF8, "application/json");
            
            var url = TimeSeriesEndoint + "custom:" + metricID;
            
            try
            {
                var httpResponse = await restClient.PutAsync(url, httpContent);
                httpResponse.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                this.healthReporter.ReportProblem("DynatraceOutput:Couldn't create timeseries: "+metricID+" ... "+ex.Message , EventFlowContextIdentifiers.Output);
                throw;
            }
        }

        private async Task<bool> SendMetric(string entityAlias,  MonitoredEntityMetrics m)
        {
            if (StartWaitMetrics.HasValue && DateTime.Now.Subtract(StartWaitMetrics.Value).TotalSeconds < 15)
            {
                this.healthReporter.ReportWarning("Skip sending metrics" , EventFlowContextIdentifiers.Output);
                return false;
            }
            else
                StartWaitMetrics = null;

            var httpContent = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");
            
            var url = MetricEndoint + "custom/" + entityAlias;
            try
            {
                var httpResponse = await restClient.PostAsync(url, httpContent);
                httpResponse.EnsureSuccessStatusCode();

                this.healthReporter.ReportHealthy();
                return true;
            }
            catch (Exception ex)
            {
                this.healthReporter.ReportProblem("DynatraceOutput:SendMetric: " + ex.ToString(), EventFlowContextIdentifiers.Output);
                StartWaitMetrics = DateTime.Now;
                return false;

            }
        }

        private async Task<CustomDeviceResponse> CreateCustomDevice(string entityAlias, MonitoredEntity m)
        {
            var httpContent = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");

            var url = MetricEndoint + "custom/" + entityAlias;
            try
            {
                var httpResponse = await restClient.PostAsync(url, httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var customDevice = JsonConvert.DeserializeObject<CustomDeviceResponse>(await httpResponse.Content.ReadAsStringAsync());

                this.healthReporter.ReportHealthy();

                return customDevice;
            }
            catch (Exception ex)
            {
                this.healthReporter.ReportProblem("DynatraceOutput:CreateCustomDevice: " + ex.ToString(), EventFlowContextIdentifiers.Output);
                return null;
            }
            
        }


        private bool TrackMetric(MonitoredEntityMetrics m, EventData e, IReadOnlyCollection<EventMetadata> metadata)
        {
            bool tracked = false;

            var ts = new EntityTimeseriesData()
            {
                timeseriesId = "custom:" + dtOutputConfiguration.TimeSeries.timeseriesId,
                dimensions = new Dictionary<string, string>()
            };

            if (metadata == null) 
            {
                object eventName = null;
                
                e.Payload.TryGetValue("EventName", out eventName);

                object eventID = null;
                if (!e.Payload.TryGetValue("ID", out eventID))
                    eventID = 0;

                ts.dimensions.Add("channel", eventName as string ?? string.Empty);
                ts.dimensions.Add("event", eventID.ToString());

                ts.dataPoints = new object[][] { new object[]
                        {(long)(e.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds,
                         1 }};
                if (m.series == null)
                    m.series = new List<EntityTimeseriesData>();
                m.series.Add(ts);
            }
            else
            {
                object eventName = null;
                foreach (EventMetadata metricMetadata in metadata)
                {
                    var result = MetricData.TryGetData(e, metricMetadata, out MetricData metricData);
                    if (result.Status != DataRetrievalStatus.Success)
                    {
                        this.healthReporter.ReportWarning("DynatraceOutput: " + result.Message, EventFlowContextIdentifiers.Output);
                        continue;
                    }

                    ts.dimensions.Add("channel", eventName as string ?? string.Empty);
                    ts.dimensions.Add("event", metricData.MetricName);
                    ts.dataPoints = new object[][] { new object[] { (long)(e.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds,
                                                                    metricData.Value }};
                    if (m.series == null)
                        m.series = new List<EntityTimeseriesData>();
                    m.series.Add(ts);

                }

            }

            tracked = true;

            return tracked;
        }

        private bool TrackEvent(MonitoredEntityMetrics m, EventData e, IReadOnlyCollection<EventMetadata> metadata)
        {
            bool tracked = false;

            var msg = new EventPushMessage()
            {
                attachRules = new PushEventAttachRules()
                {
                    entityIds = new string[] { customDeviceID.entityId }
                },
                source = "eventflow",
                start = (long)(e.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds,
                eventType = CustomEventTypes.ANNOTATION,
            };
            
           
            object eventName = null;
            if (!e.Payload.TryGetValue("EventName", out eventName))
                eventName = "";

            object eventID = null;
            if (!e.Payload.TryGetValue("ID", out eventID))
                eventID = "n/a";

            object eventMsg = null;
            if (!e.Payload.TryGetValue("Message", out eventMsg))
                eventMsg = "n/a";

            msg.annotationType = eventName.ToString() + " - " + eventID.ToString();
            msg.annotationDescription = eventMsg.ToString();
        
            if (metadata != null)
            {
                
                foreach (EventMetadata eventMetadata in metadata)
                {
                    var result = DynatraceEventData.TryGetData(e, eventMetadata, out DynatraceEventData eventData);
                    if (result.Status != DataRetrievalStatus.Success)
                    {
                        this.healthReporter.ReportWarning("DynatraceOutput: " + result.Message, EventFlowContextIdentifiers.Output);
                        continue;
                    }

                    if (!String.IsNullOrEmpty(eventData.AnnotationType))
                        msg.annotationType = eventData.AnnotationType;
                    if (!String.IsNullOrEmpty(eventData.AnnotationDescription))
                        msg.annotationDescription = eventData.AnnotationDescription;
                    if (!String.IsNullOrEmpty(eventData.Source))
                        msg.source = eventData.Source;
                    if (!String.IsNullOrEmpty(eventData.EventType))
                        msg.eventType = eventData.EventType;

                }

            }
         
            SendEvent(msg);
            tracked = true;

            return tracked;
        }

        private async void SendEvent(EventPushMessage m)
        {
            if (StartWaitEvents.HasValue && DateTime.Now.Subtract(StartWaitEvents.Value).TotalSeconds < 15)
            {
                this.healthReporter.ReportWarning("Skip sending events", EventFlowContextIdentifiers.Output);
                return;
            }
            else
                StartWaitEvents = null;

            var httpContent = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");

            try
            {
                var httpResponse = await restClient.PostAsync(EventEndoint, httpContent);
                httpResponse.EnsureSuccessStatusCode();

                this.healthReporter.ReportHealthy();
            }
            catch (Exception ex)
            {
                this.healthReporter.ReportProblem("DynatraceOutput:SendEvent: " + ex.ToString(), EventFlowContextIdentifiers.Output);
                StartWaitEvents = DateTime.Now;
            }
        }

    }
}
