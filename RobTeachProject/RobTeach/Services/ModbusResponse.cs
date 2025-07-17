namespace RobTeach.Services
{
    /// <summary>
    /// Represents the result of a Modbus operation, indicating success or failure and an accompanying message.
    /// </summary>
    public class ModbusResponse
    {
        /// <summary>
        /// Gets or sets a value indicating whether the Modbus operation was successful.
        /// </summary>
        /// <value><c>true</c> if the operation was successful; otherwise, <c>false</c>.</value>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a message providing details about the Modbus operation's result.
        /// This can be an error message in case of failure or a success confirmation.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModbusResponse"/> class.
        /// </summary>
        /// <param name="success">A boolean indicating if the operation was successful.</param>
        /// <param name="message">A message detailing the operation's result.</param>
        public ModbusResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        /// <summary>
        /// Creates a new <see cref="ModbusResponse"/> instance representing a successful operation.
        /// </summary>
        /// <param name="message">An optional message for the successful operation. Defaults to "Operation successful."</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating success.</returns>
        public static ModbusResponse Ok(string message = "Operation successful.") => new ModbusResponse(true, message);

        /// <summary>
        /// Creates a new <see cref="ModbusResponse"/> instance representing a failed operation.
        /// </summary>
        /// <param name="message">The error message detailing the failure.</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating failure.</returns>
        public static ModbusResponse Fail(string message) => new ModbusResponse(false, message);
    }
}
