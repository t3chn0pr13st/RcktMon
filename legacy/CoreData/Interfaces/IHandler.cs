using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreData.Interfaces
{
    public interface IHandler<TMessage>
    {
        /// <summary>
        /// Handles the message.
        /// </summary>
        /// <param name = "message">The message.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the asynchronous coroutine.</returns>
        Task HandleAsync(TMessage message, CancellationToken cancellationToken);
    }

    public interface IEventAggregator2
    {
        /// <summary>
        /// Searches the subscribed handlers to check if we have a handler for
        /// the message type supplied.
        /// </summary>
        /// <param name="messageType">The message type to check with</param>
        /// <returns>True if any handler is found, false if not.</returns>
        bool HandlerExistsFor(Type messageType);

        /// <summary>
        /// Subscribes an instance to all events declared through implementations of <see cref = "IHandler{TMessage}" />
        /// </summary>
        /// <param name = "subscriber">The instance to subscribe for event publication.</param>
        /// <param name = "marshal">Allows the subscriber to provide a custom thread marshaller for the message subscription.</param>
        void Subscribe(object subscriber, Func<Func<Task>, Task> marshal);

        /// <summary>
        /// Unsubscribes the instance from all events.
        /// </summary>
        /// <param name = "subscriber">The instance to unsubscribe.</param>
        void Unsubscribe(object subscriber);

        /// <summary>
        /// Publishes a message.
        /// </summary>
        /// <param name = "message">The message instance.</param>
        /// <param name = "marshal">Allows the publisher to provide a custom thread marshaller for the message publication.</param>
        /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task PublishAsync(object message, Func<Func<Task>, Task> marshal, CancellationToken cancellationToken = default);
    }
}
