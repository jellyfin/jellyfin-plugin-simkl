using System;

namespace Jellyfin.Plugin.Simkl.API.Exceptions
{
    /// <inheritdoc />
    public class InvalidTokenException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidTokenException"/> class.
        /// </summary>
        public InvalidTokenException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidTokenException"/> class.
        /// </summary>
        /// <param name="msg">The message.</param>
        public InvalidTokenException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidTokenException"/> class.
        /// </summary>
        /// <param name="msg">The message.</param>
        /// <param name="inner">The inner exception.</param>
        public InvalidTokenException(string msg, Exception inner)
            : base(msg, inner)
        {
        }
    }
}