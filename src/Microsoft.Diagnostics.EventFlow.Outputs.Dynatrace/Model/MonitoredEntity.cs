using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.EventFlow.Outputs.Dynatrace.Model
{
   
    public class MonitoredEntity
    {
        public string displayName { get; set; }
        public string[] ipAddresses { get; set; }
        public string[] listenPorts { get; set; }
        public string type { get; set; }
        public string favicon { get; set; }
        public string configUrl { get; set; }
        public string[] tags  { get; set;}
        public Dictionary<string, string> properties { get; set; }
        public List<EntityTimeseriesData> series { get; set; }

    }
}
