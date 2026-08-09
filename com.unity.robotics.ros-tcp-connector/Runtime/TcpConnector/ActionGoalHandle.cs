using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace Unity.Robotics.ROSTCPConnector
{
    /// <summary>
    /// Terminal states of an action goal, matching action_msgs/msg/GoalStatus.
    /// </summary>
    public static class ActionGoalStatus
    {
        public const int Unknown = 0;
        public const int Accepted = 1;
        public const int Executing = 2;
        public const int Canceling = 3;
        public const int Succeeded = 4;
        public const int Canceled = 5;
        public const int Aborted = 6;
    }

    /// <summary>
    /// Handed to a Unity action implementation for the lifetime of one goal.
    /// Use it to publish feedback and to notice that the caller asked to cancel.
    /// </summary>
    /// <remarks>
    /// Goals accepted here are always accepted: rejecting one would need an extra
    /// round trip before execution starts, and interfaces that can refuse a goal
    /// normally say so in the result instead. Report a refusal by filling in the
    /// result and calling <see cref="Abort"/>.
    /// </remarks>
    public class ActionGoalHandle
    {
        readonly RosTopicState m_TopicState;
        readonly int m_ActionId;

        internal ActionGoalHandle(RosTopicState topicState, int actionId)
        {
            m_TopicState = topicState;
            m_ActionId = actionId;
            Status = ActionGoalStatus.Succeeded;
        }

        /// <summary>The action name this goal arrived on.</summary>
        public string ActionName => m_TopicState.Topic;

        /// <summary>
        /// True once the caller has asked to cancel. Implementations are expected to
        /// poll this, stop early and return whatever result they have.
        /// </summary>
        /// <remarks>
        /// Set from the main thread when the cancel command arrives, so an
        /// implementation that awaits back onto the main thread sees it without
        /// any locking of its own.
        /// </remarks>
        public bool IsCancelRequested { get; internal set; }

        /// <summary>
        /// The status reported when the implementation returns. Defaults to
        /// SUCCEEDED, becomes CANCELED if the goal was cancelled, and can be set
        /// to ABORTED through <see cref="Abort"/>.
        /// </summary>
        public int Status { get; private set; }

        /// <summary>Finish this goal as aborted rather than succeeded.</summary>
        public void Abort()
        {
            Status = ActionGoalStatus.Aborted;
        }

        /// <summary>Send one feedback message for this goal.</summary>
        public void PublishFeedback<TFeedback>(TFeedback feedback) where TFeedback : Message
        {
            m_TopicState.SendActionFeedback(m_ActionId, feedback);
        }

        internal void MarkCanceled()
        {
            // A cancelled goal reports CANCELED unless the implementation
            // explicitly aborted it, which is the more specific outcome.
            if (Status != ActionGoalStatus.Aborted)
            {
                Status = ActionGoalStatus.Canceled;
            }
        }
    }
}
