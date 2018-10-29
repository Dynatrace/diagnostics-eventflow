// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Diagnostics;
using Validation;

namespace Microsoft.Diagnostics.EventFlow.Metadata
{
    public class DynatraceEventData
    {
        public static readonly string MetadataKind = "dynatrace-event";

        public string Source { get; private set; }
        public string EventType { get; private set; }
        public string AnnotationType { get; private set; }
        public string AnnotationDescription { get; private set; }

        // Ensure that DynatraceEventData can only be created using TryGetData() method
        private DynatraceEventData() { }

        public static DataRetrievalResult TryGetData(
            EventData eventData,
            EventMetadata eventMetadata,
            out DynatraceEventData meta)
        {
            Requires.NotNull(eventData, nameof(eventData));
            Requires.NotNull(eventMetadata, nameof(eventMetadata));

            meta = null;

            if (!MetadataKind.Equals(eventMetadata.MetadataType, System.StringComparison.OrdinalIgnoreCase))
            {
                return DataRetrievalResult.InvalidMetadataType(eventMetadata.MetadataType, MetadataKind);
            }

            meta = new DynatraceEventData();
            meta.AnnotationType = eventMetadata["annotationType"];
            meta.AnnotationDescription = eventMetadata["annotationDescription"];
            meta.Source = eventMetadata["source"];
            meta.EventType = eventMetadata["eventType"];

            return DataRetrievalResult.Success;
        }        
    }
}
