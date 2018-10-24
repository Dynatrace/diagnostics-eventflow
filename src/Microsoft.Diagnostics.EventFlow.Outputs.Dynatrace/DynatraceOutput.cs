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

        private HttpClient restClient;
        private readonly IHealthReporter healthReporter;

        DateTime? StartWait = null;

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

        public Task SendEventsAsync(IReadOnlyCollection<EventData> events, long transmissionSequenceNumber, CancellationToken cancellationToken)
        {
            if (this.restClient == null || events == null || events.Count == 0)
            {
                return CompletedTask;
            }

            try
            {
                MonitoredEntityConfig m = new MonitoredEntityConfig(dtOutputConfiguration.MonitoredEntity);
                if (m.properties == null)
                    m.properties = new Dictionary<string, string>();
                

                foreach (var e in events)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return CompletedTask;
                    }

                    IReadOnlyCollection<EventMetadata> metadata;
                    bool tracked = false;

                    if (e.TryGetMetadata(MetricData.MetricMetadataKind, out metadata))
                    {
                        tracked = TrackMetric(m, e, metadata);
                    }
           
                   
                    if (!tracked)
                    {
                        tracked = TrackMetric(m, e, null);
                    }
                }

                try
                {
                    if (m.series.Count > 0)
                    {
                        SendMetric(m.entityAlias, m);
                    }
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

            return CompletedTask;
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
            if (dtOutputConfiguration.TimeSeries == null)
            {
                this.healthReporter.ReportProblem($"{nameof(DynatraceOutput)}: 'TimeSeries' configuration is not set");
                return;
            }


            restClient = new HttpClient();
            restClient.BaseAddress = new Uri(dtOutputConfiguration.ServiceBaseAddress, UriKind.Absolute);
            restClient.DefaultRequestHeaders.Add("Authorization", "Api-Token "+ dtOutputConfiguration.APIToken);

            InitializeMetrics(dtOutputConfiguration);

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
                            dtOutputConfiguration.MonitoredEntity.properties.Add("vmScaleSetName", (string)meta["compute"]["vmScaleSetName"]);
                            dtOutputConfiguration.MonitoredEntity.properties.Add("zone", (string)meta["compute"]["zone"]);
                        }
                        if (useIPMeta)
                        {
                            dtOutputConfiguration.MonitoredEntity.ipAddresses = new string[]{
                                (string)meta["network"]["interface"][0]["ipv4"]["ipAddress"][0]["privateIpAddress"],
                                (string)meta["network"]["interface"][0]["ipv4"]["ipAddress"][0]["publicIpAddress"],
                            };
                        }
                        
                    }
                    catch (Exception ex)
                    {

                        this.healthReporter.ReportProblem("Couldn't resolve azure-metadata: "+ex.Message, EventFlowContextIdentifiers.Output);
                        throw;
                    }

                }
            }
            
        }

        private void InitializeMetrics(DynatraceOutputConfiguration cfg)
        {
            InitializeMetric(cfg.TimeSeries.timeseriesId, cfg.TimeSeries as TimeseriesRegistrationMessage);
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

        private async void SendMetric(string entityAlias,  MonitoredEntity m)
        {
            if (StartWait.HasValue && DateTime.Now.Subtract(StartWait.Value).TotalSeconds < 15)
            {
                this.healthReporter.ReportWarning("Skip sending metrics" , EventFlowContextIdentifiers.Output);
                return;
            }
            else
                StartWait = null;

            var httpContent = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");
            
            var url = MetricEndoint + "custom/" + entityAlias;
            try
            {

                var httpResponse = await restClient.PostAsync(url, httpContent);
                httpResponse.EnsureSuccessStatusCode();

                this.healthReporter.ReportHealthy();
            }
            catch (Exception ex)
            {
                this.healthReporter.ReportProblem("DynatraceOutput:SendMetric: " + ex.ToString(), EventFlowContextIdentifiers.Output);
                StartWait = DateTime.Now;

            }
        }


        private bool TrackMetric(MonitoredEntity m, EventData e, IReadOnlyCollection<EventMetadata> metadata)
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
       
    }
}
