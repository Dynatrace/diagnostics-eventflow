using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.EventFlow;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace eventflow
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class eventflow : StatelessService
    {
        public eventflow(StatelessServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[0];
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {

            try
            {
                using (DiagnosticPipeline pipeline = DiagnosticPipelineFactory.CreatePipeline("eventFlowConfig.json"))
                {
                    try
                    {
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }
                    }
                    catch
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Shutdown requested");
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, ex.ToString());
            }

        }

    }
}
