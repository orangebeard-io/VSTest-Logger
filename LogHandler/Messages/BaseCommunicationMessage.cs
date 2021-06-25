using System.Runtime.Serialization;

namespace Orangebeard.VSTest.TestLogger.LogHandler.Messages
{
    [DataContract]
    class BaseCommunicationMessage
    {
        [DataMember]
        public virtual CommunicationAction Action { get; set; }
    }
}
