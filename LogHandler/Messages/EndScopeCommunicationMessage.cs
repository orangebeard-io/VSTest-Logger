﻿using Orangebeard.Shared.Execution.Logging;
using System;
using System.Runtime.Serialization;

namespace Orangebeard.VSTest.TestLogger.LogHandler.Messages
{
    [DataContract]
    class EndScopeCommunicationMessage : BaseCommunicationMessage
    {
        [DataMember]
        public override CommunicationAction Action { get => CommunicationAction.EndLogScope; set => base.Action = value; }

        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public DateTime EndTime { get; set; }

        [DataMember]
        public LogScopeStatus Status { get; set; }

    }
}
