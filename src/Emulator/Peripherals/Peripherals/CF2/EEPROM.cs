//
//
//
//
//
//
using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.CF2
{
    public class EEPROM : II2CPeripheral
    {
        public EEPROM()
        {
        }

        public byte[] Read(int count = 1)
        {
            //var result = outputBuffer.ToArray();
            //this.Log(LogLevel.Noisy, "Reading {0} bytes from the device (asked for {1} bytes).", result.Length, count);
            //outputBuffer.Clear();
            this.Log(LogLevel.Noisy, "Reading 0x{0:X} bytes from EEPROM", count);
            //byte[] result = new byte[count];
            return data;
        }

        public void Write(byte[] data)
        {
         
         this.Log(LogLevel.Noisy, "Trying to write data {0:X}", data[0]);
            /*this.Log(LogLevel.Noisy, "Received {0} bytes: [{1}]", data.Length, string.Join(", ", data.Select(x => x.ToString())));
            if(!commands.TryGetCommand(data, out var command))
            {
                this.Log(LogLevel.Warning, "Unknown command: [{0}]. Ignoring the data.", string.Join(", ", data.Select(x => string.Format("0x{0:X}", x))));
                return;
            }
            command(data);*/
        }

        public void FinishTransmission()
        {
        }

        public void Reset()
        {
        }

        private byte[] data = {81,42,70};
    }
}