using System.IO;
using JetBrains.Annotations;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    public abstract class SysCommand
    {
        public const string k_SysCommand_Handshake = "__handshake";
        public const string k_SysCommand_Log = "__log";
        public const string k_SysCommand_Warning = "__warn";
        public const string k_SysCommand_Error = "__error";
        public const string k_SysCommand_ServiceRequest = "__request";
        public const string k_SysCommand_ServiceResponse = "__response";
        public const string k_SysCommand_Subscribe = "__subscribe";
        public const string k_SysCommand_Publish = "__publish";
        public const string k_SysCommand_RosService = "__ros_service";
        public const string k_SysCommand_UnityService = "__unity_service";
        public const string k_SysCommand_TopicList = "__topic_list";
        public const string k_SysCommand_RemoveSubscriber = "__remove_subscriber";
        public const string k_SysCommand_RemovePublisher = "__remove_publisher";
        public const string k_SysCommand_RemoveRosService = "__remove_ros_service";
        public const string k_SysCommand_RemoveUnityService = "__remove_unity_service";
        public const string k_SysCommand_Ping = "__ping";
        public const string k_SysCommand_PingResponse = "__ping_response";

        // Action servers implemented on the Unity side. The endpoint owns the ROS
        // action server and forwards each goal here; feedback and the final result
        // travel back the other way, tagged with the same action_id.
        public const string k_SysCommand_UnityAction = "__unity_action";
        public const string k_SysCommand_RemoveUnityAction = "__remove_unity_action";
        public const string k_SysCommand_ActionGoal = "__action_goal";
        public const string k_SysCommand_ActionCancel = "__action_cancel";
        public const string k_SysCommand_ActionFeedback = "__action_feedback";
        public const string k_SysCommand_ActionResult = "__action_result";

        public abstract string Command
        {
            get;
        }

        public virtual object BuildParam()
        {
            return this;

        }
        public void PopulateSysCommand(MessageSerializer messageSerializer)
        {
            messageSerializer.Clear();
            // syscommands are sent as:
            // 4 byte command length, followed by that many bytes of the command
            // (all command names start with __ to distinguish them from ros topics)
            messageSerializer.Write(Command);
            // 4-byte json length, followed by a json string of that length
            string json = JsonUtility.ToJson(BuildParam());
            messageSerializer.WriteUnaligned(json);
        }

        public void SendTo([NotNull] Stream stream, MessageSerializer messageSerializer = null)
        {
            if (messageSerializer == null)
            {
                messageSerializer = new MessageSerializer();
            }

            PopulateSysCommand(messageSerializer);
            messageSerializer.SendTo(stream);
        }
    }

    public struct SysCommand_Topic
    {
        public string topic;
    }

    public struct SysCommand_TopicAndType
    {
        public string topic;
        public string message_name;
    }

    // For backwards compatibility, we encode the handshake in two stages:
    // Stage 1 - which must NEVER change - is just a version string and a metadata string.
    public struct SysCommand_Handshake
    {
        public string version;
        public string metadata;
    }

    // Stage 2 is the json encoded contents of the metadata string.
    // Because this structure may change with future versions of ROS TCP Connector, we only decode it
    // after checking the version number is correct.
    public struct SysCommand_Handshake_Metadata
    {
        public string protocol; // "ROS1" or "ROS2"
    }

    public struct SysCommand_Log
    {
        public string text;
    }

    public struct SysCommand_Service
    {
        public int srv_id;
    }

    // Identifies one in-flight action goal. The endpoint allocates the id when it
    // forwards a goal, and every later message about that goal carries it back.
    public struct SysCommand_Action
    {
        public int action_id;
    }

    // A cancel request is not followed by a message, so unlike __action_goal it has
    // to name the action itself rather than relying on the payload's destination.
    public struct SysCommand_ActionCancel
    {
        public string topic;
        public int action_id;
    }

    // As above, plus the terminal state of the goal, using the values from
    // action_msgs/msg/GoalStatus (SUCCEEDED = 4, CANCELED = 5, ABORTED = 6).
    public struct SysCommand_ActionResult
    {
        public int action_id;
        public int status;
        // False when the Unity side had no result to send (it threw, say). The
        // endpoint then fills in a default result and, importantly, does not wait
        // for a message to follow this command.
        public bool has_result;
    }

    public struct SysCommand_TopicsRequest
    {
    }

    public struct SysCommand_TopicsResponse
    {
        public string[] topics;
        public string[] types;
    }

    public struct SysCommand_PingRequest
    {
        public string request_time;
    }

    public struct SysCommand_PingResponse
    {
        public string request_time;
    }

    public struct SysCommand_PublisherRegistration
    {
        public string topic;
        public string message_name;
        public int queue_size;
        public bool latch;
    }
}
