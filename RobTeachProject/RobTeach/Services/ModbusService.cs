using EasyModbus;
using RobTeach.Models;
using RobTeach.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using IxMilia.Dxf.Entities;

namespace RobTeach.Services
{
    public record ModbusReadInt16Result(bool Success, short Value, string Message)
    {
        public static ModbusReadInt16Result Ok(short value, string message = "Read successful.") => new ModbusReadInt16Result(true, value, message);
        public static ModbusReadInt16Result Fail(string message, short defaultValue = 0) => new ModbusReadInt16Result(false, defaultValue, message);
    }

    /// <summary>
    /// Provides services for communicating with a Modbus TCP server (e.g., a robot controller).
    /// Handles connection, disconnection, and sending configuration data.
    /// </summary>
    public class ModbusService
    {
        private ModbusClient? modbusClient; // The EasyModbus client instance

        // Define Modbus register addresses based on the application's README or device specification.
        // These constants define the memory map on the Modbus server (robot).
        private const int TrajectoryCountRegister = 4000;   // Register to write the number of trajectories being sent.
        private const int BasePointsCountRegister = 4001;   // Base register for the point count of the first trajectory.
        private const int BaseXCoordsRegister = 4002;       // Base register for X coordinates of the first trajectory.
        private const int BaseYCoordsRegister = 4052;       // Base register for Y coordinates of the first trajectory.
        private const int BaseNozzleNumRegister = 4102;     // Base register for nozzle number of the first trajectory.
        private const int BaseSprayTypeRegister = 4103;     // Base register for spray type of the first trajectory.

        private const int TrajectoryRegisterOffset = 100;   // Offset between base registers of consecutive trajectories.
        private const int MaxPointsPerTrajectory = 50;      // Maximum number of points per trajectory supported by the robot.
        private const int MaxTrajectories = 5;              // Maximum number of trajectories supported by the robot.

        /// <summary>
        /// Attempts to connect to the Modbus TCP server at the specified IP address and port.
        /// </summary>
        /// <param name="ipAddress">The IP address of the Modbus server.</param>
        /// <param name="port">The port number of the Modbus server.</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating the success or failure of the connection attempt.</returns>
        public ModbusResponse Connect(string ipAddress, int port)
        {
            try
            {
                modbusClient = new ModbusClient(ipAddress, port);
                modbusClient.ConnectionTimeout = 2000; // Set connection timeout to 2 seconds.

                // Note: EasyModbus typically handles send/receive timeouts internally for its operations.
                // If more granular control is needed, it might require a different library or direct socket manipulation.

                modbusClient.Connect(); // Attempt to establish the connection.
                if (modbusClient.Connected)
                {
                    return ModbusResponse.Ok($"Successfully connected to Modbus server at {ipAddress}:{port}.");
                }
                else
                {
                    // This path might be less common if Connect() throws an exception on failure.
                    return ModbusResponse.Fail("Connection failed: ModbusClient reported not connected after Connect() call.");
                }
            }
            catch (Exception ex) // Catch any exception during connection (e.g., network issues, server not responding).
            {
                Debug.WriteLine($"[ModbusService] Connection error to {ipAddress}:{port}: {ex.ToString()}");
                return ModbusResponse.Fail($"Connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnects from the Modbus TCP server if a connection is active.
        /// </summary>
        public void Disconnect()
        {
            if (modbusClient != null && modbusClient.Connected)
            {
                try
                {
                    modbusClient.Disconnect();
                }
                catch (Exception ex) // Catch potential errors during disconnection.
                {
                    Debug.WriteLine($"[ModbusService] Disconnect error: {ex.ToString()}");
                    // Depending on requirements, this error might be surfaced to the UI or just logged.
                }
                // modbusClient = null; // Optionally nullify for garbage collection, though IsConnected will be false.
            }
        }

        /// <summary>
        /// Gets a value indicating whether the Modbus client is currently connected.
        /// </summary>
        public bool IsConnected => modbusClient != null && modbusClient.Connected;

        /// <summary>
        /// Sends the specified robot <see cref="Configuration"/> (trajectories and parameters) to the connected Modbus server.
        /// </summary>
        /// <param name="config">The <see cref="Configuration"/> to send.</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating the success or failure of the send operation.</returns>
        public ModbusResponse SendConfiguration(Models.Configuration config)
        {
            if (!IsConnected) return ModbusResponse.Fail("Error: Not connected to Modbus server. Please connect first.");

            if (config == null) return ModbusResponse.Fail("Error: Configuration is null.");
            if (config.SprayPasses == null || !config.SprayPasses.Any()) return ModbusResponse.Fail("Error: No spray passes available in the configuration.");
            if (config.CurrentPassIndex < 0 || config.CurrentPassIndex >= config.SprayPasses.Count) return ModbusResponse.Fail($"Error: Invalid CurrentPassIndex ({config.CurrentPassIndex}). No active spray pass selected or index out of bounds.");

            var dataQueue = new Queue<float>();
            SprayPass currentPass = config.SprayPasses[config.CurrentPassIndex];

            // Populate queue with data
            dataQueue.Enqueue((float)config.SprayPasses.Count);

            foreach (var pass in config.SprayPasses)
            {
                int totalPrimitives = pass.Trajectories.Sum(t => t.PrimitiveType == "Polygon" ? (t.Points.Count - ((t.OriginalDxfEntity as DxfLwPolyline)?.IsClosed ?? false ? 0 : 1)) : 1);
                dataQueue.Enqueue((float)totalPrimitives);

                int primitiveIndexInPass = 0;
                foreach (var trajectory in pass.Trajectories)
                {
                    if (trajectory.PrimitiveType != "Polygon")
                    {
                        primitiveIndexInPass++;
                        dataQueue.Enqueue((float)primitiveIndexInPass);

                        float primitiveType = 0.0f;
                        if (trajectory.PrimitiveType == "Line") primitiveType = 1.0f;
                        else if (trajectory.PrimitiveType == "Circle") primitiveType = 2.0f;
                        else if (trajectory.PrimitiveType == "Arc") primitiveType = 3.0f;
                        dataQueue.Enqueue(primitiveType);
                    }

                    if (trajectory.PrimitiveType != "Polygon")
                    {
                        dataQueue.Enqueue(trajectory.UpperNozzleGasOn ? 11.0f : 10.0f);
                        dataQueue.Enqueue(trajectory.UpperNozzleLiquidOn ? 12.0f : 10.0f);
                        dataQueue.Enqueue(trajectory.LowerNozzleGasOn ? 21.0f : 20.0f);
                        dataQueue.Enqueue(trajectory.LowerNozzleLiquidOn ? 22.0f : 20.0f);

                        double lengthInMeters = TrajectoryUtils.CalculateTrajectoryLength(trajectory);
                        double currentRuntime = trajectory.Runtime;
                        float speedForRobot = 0.0f;

                        if (lengthInMeters > 0.00001 && currentRuntime > 0.00001)
                        {
                            speedForRobot = (float)(lengthInMeters / currentRuntime);
                        }
                        dataQueue.Enqueue(speedForRobot);
                    }

                    if (trajectory.PrimitiveType == "Line")
                    {
                        dataQueue.Enqueue((float)trajectory.LineStartPoint.X);
                        dataQueue.Enqueue((float)trajectory.LineStartPoint.Y);
                        dataQueue.Enqueue((float)trajectory.LineStartPoint.Z);
                        dataQueue.Enqueue(0f); dataQueue.Enqueue(0f); dataQueue.Enqueue(0f);
                        dataQueue.Enqueue((float)trajectory.LineEndPoint.X);
                        dataQueue.Enqueue((float)trajectory.LineEndPoint.Y);
                        dataQueue.Enqueue((float)trajectory.LineEndPoint.Z);
                        dataQueue.Enqueue(0f); dataQueue.Enqueue(0f); dataQueue.Enqueue(0f);
                    }
                    else if (trajectory.PrimitiveType == "Arc")
                    {
                        dataQueue.Enqueue((float)trajectory.ArcPoint1.Coordinates.X);
                        dataQueue.Enqueue((float)trajectory.ArcPoint1.Coordinates.Y);
                        dataQueue.Enqueue((float)trajectory.ArcPoint1.Coordinates.Z);
                        dataQueue.Enqueue((float)trajectory.ArcPoint1.Rx);
                        dataQueue.Enqueue((float)trajectory.ArcPoint1.Ry);
                        dataQueue.Enqueue((float)trajectory.ArcPoint1.Rz);
                        dataQueue.Enqueue((float)trajectory.ArcPoint2.Coordinates.X);
                        dataQueue.Enqueue((float)trajectory.ArcPoint2.Coordinates.Y);
                        dataQueue.Enqueue((float)trajectory.ArcPoint2.Coordinates.Z);
                        dataQueue.Enqueue((float)trajectory.ArcPoint2.Rx);
                        dataQueue.Enqueue((float)trajectory.ArcPoint2.Ry);
                        dataQueue.Enqueue((float)trajectory.ArcPoint2.Rz);
                        dataQueue.Enqueue((float)trajectory.ArcPoint3.Coordinates.X);
                        dataQueue.Enqueue((float)trajectory.ArcPoint3.Coordinates.Y);
                        dataQueue.Enqueue((float)trajectory.ArcPoint3.Coordinates.Z);
                        dataQueue.Enqueue((float)trajectory.ArcPoint3.Rx);
                        dataQueue.Enqueue((float)trajectory.ArcPoint3.Ry);
                        dataQueue.Enqueue((float)trajectory.ArcPoint3.Rz);
                    }
                    else if (trajectory.PrimitiveType == "Circle")
                    {
                        dataQueue.Enqueue((float)trajectory.CirclePoint1.Coordinates.X);
                        dataQueue.Enqueue((float)trajectory.CirclePoint1.Coordinates.Y);
                        dataQueue.Enqueue((float)trajectory.CirclePoint1.Coordinates.Z);
                        dataQueue.Enqueue((float)trajectory.CirclePoint1.Rx);
                        dataQueue.Enqueue((float)trajectory.CirclePoint1.Ry);
                        dataQueue.Enqueue((float)trajectory.CirclePoint1.Rz);
                        dataQueue.Enqueue((float)trajectory.CirclePoint2.Coordinates.X);
                        dataQueue.Enqueue((float)trajectory.CirclePoint2.Coordinates.Y);
                        dataQueue.Enqueue((float)trajectory.CirclePoint2.Coordinates.Z);
                        dataQueue.Enqueue((float)trajectory.CirclePoint2.Rx);
                        dataQueue.Enqueue((float)trajectory.CirclePoint2.Ry);
                        dataQueue.Enqueue((float)trajectory.CirclePoint2.Rz);
                        dataQueue.Enqueue((float)trajectory.CirclePoint3.Coordinates.X);
                        dataQueue.Enqueue((float)trajectory.CirclePoint3.Coordinates.Y);
                        dataQueue.Enqueue((float)trajectory.CirclePoint3.Coordinates.Z);
                        dataQueue.Enqueue((float)trajectory.CirclePoint3.Rx);
                        dataQueue.Enqueue((float)trajectory.CirclePoint3.Ry);
                        dataQueue.Enqueue((float)trajectory.CirclePoint3.Rz);
                    }

                    if (trajectory.PrimitiveType != "Polygon")
                    {
                        for (int j = 0; j < 3; j++) dataQueue.Enqueue(0.0f);
                    }
                }
            }

            try
            {
                int currentAddress = 4000;
                var registers = new List<int>();
                while(dataQueue.Count > 0)
                {
                    float data = dataQueue.Dequeue();
                    registers.AddRange(ModbusClient.ConvertFloatToRegisters(data));
                }
                if (registers.Count > 0)
                {
                    const int chunkSize = 100; // Send 100 registers at a time
                    for (int i = 0; i < registers.Count; i += chunkSize)
                    {
                        var chunk = registers.Skip(i).Take(chunkSize).ToArray();
                        modbusClient.WriteMultipleRegisters(currentAddress + i, chunk);
                    }
                }

                return ModbusResponse.Ok($"Successfully sent configuration to Modbus server.");
            }
            // Handle specific exceptions from the Modbus library if they are known and provide distinct information.
            catch (System.IO.IOException ioEx) // Often indicates network or stream-related issues.
            {
                 Debug.WriteLine($"[ModbusService] IO error during send: {ioEx.ToString()}");
                 return ModbusResponse.Fail($"IO error sending Modbus data: {ioEx.Message}");
            }
            catch (EasyModbus.Exceptions.ModbusException modEx) // Catch specific Modbus protocol errors.
            {
                 Debug.WriteLine($"[ModbusService] Modbus protocol error during send: {modEx.ToString()}");
                 return ModbusResponse.Fail($"Modbus protocol error: {modEx.Message}");
            }
            catch (Exception ex) // Catch any other unexpected errors during the send operation.
            {
                Debug.WriteLine($"[ModbusService] General error during send: {ex.ToString()}");
                return ModbusResponse.Fail($"An unexpected error occurred while sending Modbus data: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads a single 16-bit signed integer from a Modbus holding register.
        /// </summary>
        /// <param name="address">The Modbus address (0-based) of the holding register to read.</param>
        /// <returns>A <see cref="ModbusReadInt16Result"/> containing the outcome of the read operation.</returns>
        public ModbusReadInt16Result ReadHoldingRegisterInt16(ushort address)
        {
            if (!IsConnected)
            {
                return ModbusReadInt16Result.Fail("Error: Not connected to Modbus server.");
            }

            try
            {
                // Read one holding register. EasyModbus uses 0-based addressing for parameters if matching PLC,
                // but the ReadHoldingRegisters function itself might expect standard Modbus (1-based for UI, 0-based for protocol).
                // Assuming 'address' parameter is 0-based as per common Modbus library usage for actual register number.
                // If the documentation meant address 1000 as seen in a Modbus tool (1-based), it would be register 999 (0-based).
                // For now, we'll assume the passed 'address' is the correct 0-based register number.
                int[] registers = modbusClient!.ReadHoldingRegisters(address, 1); // Read 1 register

                if (registers != null && registers.Length == 1)
                {
                    // Modbus registers are 16-bit. An int from ReadHoldingRegisters is likely a .NET int (32-bit)
                    // but holds a 16-bit value. We need to cast to short for signed 16-bit.
                    short value = (short)registers[0];
                    return ModbusReadInt16Result.Ok(value, $"Successfully read value {value} from address {address}.");
                }
                else
                {
                    return ModbusReadInt16Result.Fail($"Modbus read error: No data or unexpected data length received from address {address}.");
                }
            }
            catch (System.IO.IOException ioEx)
            {
                 Debug.WriteLine($"[ModbusService] IO error during read from address {address}: {ioEx.ToString()}");
                 return ModbusReadInt16Result.Fail($"IO error reading Modbus data from address {address}: {ioEx.Message}");
            }
            catch (EasyModbus.Exceptions.ModbusException modEx)
            {
                 Debug.WriteLine($"[ModbusService] Modbus protocol error during read from address {address}: {modEx.ToString()}");
                 return ModbusReadInt16Result.Fail($"Modbus protocol error reading from address {address}: {modEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModbusService] General error during read from address {address}: {ex.ToString()}");
                return ModbusReadInt16Result.Fail($"An unexpected error occurred while reading Modbus data from address {address}: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a single 16-bit signed integer to a Modbus holding register.
        /// </summary>
        /// <param name="address">The Modbus address (0-based) of the holding register to write.</param>
        /// <param name="value">The short (Int16) value to write.</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating the success or failure of the write operation.</returns>
        public ModbusResponse WriteSingleShortRegister(ushort address, short value)
        {
            if (!IsConnected)
            {
                return ModbusResponse.Fail("Error: Not connected to Modbus server.");
            }

            try
            {
                // EasyModbus WriteSingleRegister takes int for address and value.
                // The value is treated as a 16-bit word.
                modbusClient!.WriteSingleRegister(address, value);
                return ModbusResponse.Ok($"Successfully wrote value {value} to address {address}.");
            }
            catch (System.IO.IOException ioEx)
            {
                 Debug.WriteLine($"[ModbusService] IO error during write to address {address}: {ioEx.ToString()}");
                 return ModbusResponse.Fail($"IO error writing Modbus data to address {address}: {ioEx.Message}");
            }
            catch (EasyModbus.Exceptions.ModbusException modEx)
            {
                 Debug.WriteLine($"[ModbusService] Modbus protocol error during write to address {address}: {modEx.ToString()}");
                 return ModbusResponse.Fail($"Modbus protocol error writing to address {address}: {modEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModbusService] General error during write to address {address}: {ex.ToString()}");
                return ModbusResponse.Fail($"An unexpected error occurred while writing Modbus data to address {address}: {ex.Message}");
            }
        }
    }
}
