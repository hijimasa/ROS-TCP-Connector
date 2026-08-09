using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    public class RosTopicState
    {
        string m_Topic;
        public string Topic => m_Topic;

        MessageSubtopic m_Subtopic;
        public MessageSubtopic Subtopic => m_Subtopic;

        string m_RosMessageName;
        public string RosMessageName => m_RosMessageName;

        TopicMessageSender m_MessageSender;
        public TopicMessageSender MessageSender => m_MessageSender;
        public bool IsPublisher { get; private set; }
        public bool IsPublisherLatched { get; private set; }
        public bool SentPublisherRegistration { get; private set; }

        ROSConnection m_Connection;
        public ROSConnection Connection => m_Connection;
        ROSConnection.InternalAPI m_ConnectionInternal;
        Func<MessageDeserializer, Message> m_Deserializer;

        Func<Message, Message> m_ServiceImplementation;
        Func<Message, Task<Message>> m_ServiceImplementationAsync;

        RosTopicState m_ServiceResponseTopic;
        public RosTopicState ServiceResponseTopic => m_ServiceResponseTopic;

        bool m_IsRosService;
        public bool IsRosService => m_IsRosService;
        public bool IsUnityService => m_ServiceImplementation != null || m_ServiceImplementationAsync != null;
        public bool IsService => m_ServiceResponseTopic != null || m_Subtopic == MessageSubtopic.Response;

        // Action server implemented on the Unity side. Incoming messages on this
        // topic are goals; feedback and results go back out on the same name,
        // tagged by the sys command that precedes them.
        Func<Message, ActionGoalHandle, Task<Message>> m_ActionImplementation;
        public bool IsUnityAction => m_ActionImplementation != null;
        readonly Dictionary<int, ActionGoalHandle> m_ActiveGoals = new Dictionary<int, ActionGoalHandle>();

        List<Action<Message>> m_SubscriberCallbacks = new List<Action<Message>>();
        public bool HasSubscriberCallback => m_SubscriberCallbacks.Count > 0;
        public bool SentSubscriberRegistration { get; private set; }

        float m_LastMessageReceivedRealtime;
        float m_LastMessageSentRealtime;
        public float LastMessageReceivedRealtime => m_LastMessageReceivedRealtime;
        public float LastMessageSentRealtime => m_LastMessageSentRealtime;

        internal RosTopicState(string topic, string rosMessageName, ROSConnection connection, ROSConnection.InternalAPI connectionInternal, bool isService, MessageSubtopic subtopic = MessageSubtopic.Default)
        {
            m_Topic = topic;
            m_Subtopic = subtopic;
            m_RosMessageName = rosMessageName;
            m_Connection = connection;
            m_ConnectionInternal = connectionInternal;
            if (isService && subtopic == MessageSubtopic.Default)
            {
                m_ServiceResponseTopic = new RosTopicState(topic, rosMessageName, m_Connection, m_ConnectionInternal, isService, MessageSubtopic.Response);
            }
        }

        internal void ChangeRosMessageName(string rosMessageName)
        {
            if (m_RosMessageName != null)
                Debug.LogWarning($"Inconsistent declaration of topic '{Topic}': was '{m_RosMessageName}', switching to '{rosMessageName}'.");
            m_RosMessageName = rosMessageName;
            // force deserializer to be refreshed
            m_Deserializer = null;
        }

        internal void OnMessageReceived(byte[] data)
        {
            m_LastMessageReceivedRealtime = Time.realtimeSinceStartup;
            if (m_IsRosService && m_ServiceResponseTopic != null)
            {
                //  For a service, incoming messages are a different type from outgoing messages.
                //  We process them using a separate RosTopicState.
                m_ServiceResponseTopic.OnMessageReceived(data);
                return;
            }

            // don't bother deserializing this message if nobody cares
            if (m_SubscriberCallbacks.Count == 0)
            {
                return;
            }

            Message message = Deserialize(data);

            m_SubscriberCallbacks.ForEach(item => item(message));
        }

        void OnMessageSent(Message message)
        {
            m_LastMessageSentRealtime = ROSConnection.s_RealTimeSinceStartup;
            if (m_RosMessageName == null)
            {
                ChangeRosMessageName(message.RosMessageName);
            }

            m_SubscriberCallbacks.ForEach(item => item(message));
        }

        internal async void HandleUnityServiceRequest(byte[] data, int serviceId)
        {
            if (!IsUnityService)
            {
                Debug.LogError($"Unity service '{m_Topic}' has not been implemented!");
                return;
            }

            OnMessageReceived(data);

            // deserialize the request message
            Message requestMessage = Deserialize(data);

            // run the actual service
            Message response;

            if (m_ServiceImplementationAsync != null)
            {
                response = await m_ServiceImplementationAsync(requestMessage);
            }
            else
            {
                response = m_ServiceImplementation(requestMessage);
            }

            m_ServiceResponseTopic.OnMessageSent(response);

            // send the response message back
            m_ConnectionInternal.SendUnityServiceResponse(serviceId);
            m_MessageSender.Queue(response);
            m_ConnectionInternal.AddSenderToQueue(m_MessageSender);
        }

        Message Deserialize(byte[] data)
        {
            if (m_Deserializer == null)
                m_Deserializer = MessageRegistry.GetDeserializeFunction(m_RosMessageName, m_Subtopic);

            m_ConnectionInternal.Deserializer.InitWithBuffer(data);
            return m_Deserializer(m_ConnectionInternal.Deserializer);
        }

        public void AddSubscriber(Action<Message> callback)
        {
            m_SubscriberCallbacks.Add(callback);

            RegisterSubscriber();
        }

        void RegisterSubscriber(NetworkStream stream = null)
        {
            if (m_Connection.HasConnectionThread && !SentSubscriberRegistration && !IsService)
            {
                m_ConnectionInternal.SendSubscriberRegistration(m_Topic, m_RosMessageName, stream);
                SentSubscriberRegistration = true;
            }
        }

        public void UnsubscribeAll()
        {
            m_SubscriberCallbacks.Clear();
            m_ConnectionInternal.SendSubscriberUnregistration(m_Topic);
            SentSubscriberRegistration = false;
        }

        public void ImplementService<TRequest, TResponse>(Func<TRequest, TResponse> implementation, int queueSize)
            where TRequest : Message
            where TResponse : Message
        {
            m_ServiceImplementation = (Message msg) =>
            {
                return implementation((TRequest)msg);
            };
            m_ConnectionInternal.SendUnityServiceRegistration(m_Topic, m_RosMessageName);
            CreateMessageSender(queueSize);
        }

        public void ImplementService<TRequest, TResponse>(Func<TRequest, Task<TResponse>> implementation, int queueSize)
            where TRequest : Message
            where TResponse : Message
        {
            m_ServiceImplementationAsync = async (Message msg) =>
            {
                TResponse response = await implementation((TRequest)msg);
                return response;
            };
            m_ConnectionInternal.SendUnityServiceRegistration(m_Topic, m_RosMessageName);
            CreateMessageSender(queueSize);
        }

        public void ImplementAction<TGoal, TResult>(Func<TGoal, ActionGoalHandle, Task<TResult>> implementation)
            where TGoal : Message
            where TResult : Message
        {
            m_ActionImplementation = async (Message msg, ActionGoalHandle handle) =>
            {
                TResult result = await implementation((TGoal)msg, handle);
                return result;
            };
            m_ConnectionInternal.SendUnityActionRegistration(m_Topic, m_RosMessageName);
        }

        public void UnimplementAction()
        {
            if (!IsUnityAction)
            {
                return;
            }
            m_ActionImplementation = null;
            m_ActiveGoals.Clear();
            m_ConnectionInternal.SendUnityActionUnregistration(m_Topic);
        }

        /// <summary>
        /// Run one goal that the endpoint forwarded, then report the result.
        /// </summary>
        /// <remarks>
        /// async void for the same reason as HandleUnityServiceRequest: this is
        /// invoked from the connection's Update pump, which has nothing to await on.
        /// Exceptions are turned into an ABORTED result rather than being allowed to
        /// escape, because a goal that never reports back leaves the ROS side waiting
        /// forever.
        /// </remarks>
        internal async void HandleActionGoal(byte[] data, int actionId)
        {
            if (!IsUnityAction)
            {
                Debug.LogError($"Unity action '{m_Topic}' has not been implemented!");
                return;
            }

            OnMessageReceived(data);
            Message goalMessage = Deserialize(data);

            ActionGoalHandle handle = new ActionGoalHandle(this, actionId);
            m_ActiveGoals[actionId] = handle;

            Message result = null;
            try
            {
                result = await m_ActionImplementation(goalMessage, handle);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unity action '{m_Topic}' threw: {e}");
                handle.Abort();
            }
            finally
            {
                m_ActiveGoals.Remove(actionId);
            }

            if (handle.IsCancelRequested)
            {
                handle.MarkCanceled();
            }

            if (result == null)
            {
                // Nothing to send back, but the endpoint must still be told the goal
                // is over. It fills in a default result for the action type.
                m_ConnectionInternal.SendActionResult(actionId, ActionGoalStatus.Aborted, m_Topic, null);
                return;
            }
            m_ConnectionInternal.SendActionResult(actionId, handle.Status, m_Topic, result);
        }

        internal void HandleActionCancel(int actionId)
        {
            ActionGoalHandle handle;
            if (!m_ActiveGoals.TryGetValue(actionId, out handle))
            {
                // The goal finished before the cancel arrived. Nothing to do.
                return;
            }
            handle.IsCancelRequested = true;
        }

        internal void SendActionFeedback(int actionId, Message feedback)
        {
            m_LastMessageSentRealtime = ROSConnection.s_RealTimeSinceStartup;
            m_ConnectionInternal.SendActionFeedback(actionId, m_Topic, feedback);
        }

        public void RegisterPublisher(int queueSize, bool latch)
        {
            if (IsPublisher)
            {
                Debug.LogWarning($"Publisher for topic {m_Topic} registered twice!");
                return;
            }
            IsPublisher = true;
            IsPublisherLatched = latch;
            m_ConnectionInternal.SendPublisherRegistration(m_Topic, m_RosMessageName, queueSize, latch);
            CreateMessageSender(queueSize);
        }

        /// <summary>
        /// Stop publishing this topic and tell the endpoint to drop its publisher.
        /// The counterpart of UnsubscribeAll for the publishing direction.
        /// </summary>
        /// <remarks>
        /// The message sender is kept: an outgoing queue may still hold a
        /// reference to it, and a later RegisterPublisher replaces it anyway.
        /// Clearing IsPublisher is what stops OnConnectionEstablished from
        /// re-registering this topic after a reconnect.
        /// </remarks>
        public void UnregisterPublisher()
        {
            if (!IsPublisher)
            {
                return;
            }
            IsPublisher = false;
            IsPublisherLatched = false;
            m_ConnectionInternal.SendPublisherUnregistration(m_Topic);
        }

        public void Publish(Message message)
        {
            m_LastMessageSentRealtime = ROSConnection.s_RealTimeSinceStartup;
            OnMessageSent(message);
            m_MessageSender.Queue(message);
            m_ConnectionInternal.AddSenderToQueue(m_MessageSender);
        }

        void CreateMessageSender(int queueSize)
        {
            m_MessageSender = new TopicMessageSender(Topic, m_RosMessageName, queueSize);
        }

        public void SetMessagePool(IMessagePool messagePool)
        {
            m_MessageSender.SetMessagePool(messagePool);
        }

        public void RegisterRosService(string responseMessageName, int queueSize)
        {
            m_IsRosService = true;
            m_ConnectionInternal.SendRosServiceRegistration(m_Topic, m_RosMessageName);
            CreateMessageSender(queueSize);
        }

        internal void SendServiceRequest(Message requestMessage, int serviceId)
        {
            m_ConnectionInternal.SendServiceRequest(serviceId);
            m_MessageSender.Queue(requestMessage);
            m_ConnectionInternal.AddSenderToQueue(m_MessageSender);
            OnMessageSent(requestMessage);
        }

        internal void OnConnectionEstablished(NetworkStream stream)
        {
            if (m_SubscriberCallbacks.Count > 0 && !SentSubscriberRegistration)
            {
                m_ConnectionInternal.SendSubscriberRegistration(m_Topic, m_RosMessageName, stream);
                SentSubscriberRegistration = true;
            }

            if (IsUnityService)
            {
                m_ConnectionInternal.SendUnityServiceRegistration(m_Topic, m_RosMessageName, stream);
            }

            if (IsPublisher)
            {
                //Register the publisher before sending anything.
                m_ConnectionInternal.SendPublisherRegistration(m_Topic, m_RosMessageName, m_MessageSender.QueueSize, IsPublisherLatched, stream);
                if (IsPublisherLatched)
                {
                    m_MessageSender.PrepareLatchMessage();
                    m_ConnectionInternal.AddSenderToQueue(m_MessageSender);
                }
            }

            if (m_IsRosService)
            {
                m_ConnectionInternal.SendRosServiceRegistration(m_Topic, m_RosMessageName, stream);
            }
        }

        internal void OnConnectionLost()
        {
            SentSubscriberRegistration = false;
        }
    }
}
