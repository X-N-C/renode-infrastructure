//
// Copyright (c) 2010-2020 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Linq;
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Peripherals.Sensors;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.CF2
{
    public class BMP388 : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>, ISensor
    {
        public BMP388()
        {
            fifoP = new SensorSamplesFifo<ScalarSample>();
            fifoT = new SensorSamplesFifo<ScalarSample>();
            RegistersCollection = new ByteRegisterCollection(this);
            Int1 = new GPIO();
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
            Int1.Unset();
            this.Log(LogLevel.Noisy, "Reset registers");
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0)
            {
                this.Log(LogLevel.Warning, "Unexpected write with no data");
                return;
            }

            this.Log(LogLevel.Noisy, "Write with {0} bytes of data: {1}", data.Length, Misc.PrettyPrintCollectionHex(data));
            registerAddress = (Registers)data[0];

            // length=1 preparing to read
            // length=3 one byte of data
            // (n*2)+2 burst write with n bytes

            // skip the first byte as it contains register address
            // Must skip final byte, problem with I2C

            if(data.Length == 1)
            {
                this.Log(LogLevel.Noisy, "Preparing to read register {0} (0x{0:X})", registerAddress);
            }
            else if(data.Length == 3)
            {
                RegistersCollection.Write((byte)registerAddress, data[1]);
                this.Log(LogLevel.Noisy, "Writing one byte 0x{0:X} to register {1} (0x{1:X})", data[1], registerAddress);
            }
            else
            {
                // Burst write causes one extra trash byte to be transmitted in addition
                // to the extra I2C byte.
                this.Log(LogLevel.Noisy, "Burst write mode!");
                for(var i = 0; 2*i < data.Length-2; i++)
                {
                    RegistersCollection.Write(data[2*i], data[2*i+1]);
                    this.Log(LogLevel.Noisy, "Writing 0x{0:X} to register {1} (0x{1:X})", data[2*i+1], (Registers)data[2*i]);
                }
            }
        }

        // Help function
        public byte ReadRegister(byte offset)
        {
            return RegistersCollection.Read(offset);
        }

        public byte[] Read(int count)
        {
            // Need a semaphore?
            if(registerAddress==Registers.Data0)
            {
                fifoP.TryDequeueNewSample();
                fifoT.TryDequeueNewSample();
            }

            var result = new byte[registerAddress==Registers.Data0?6 : registerAddress==Registers.OSR?4 : 1 ];
            for(var i = 0; i < result.Length; i++)
            {
                result[i] = RegistersCollection.Read((byte)registerAddress + i);
                this.Log(LogLevel.Noisy, "Read value 0x{0:X} from register {1} (0x{1:X})", result[i], (Registers)registerAddress + i);
            }
            return result;
        }

        public void FinishTransmission()
        {
        }

        public ByteRegisterCollection RegistersCollection { get; }
        public GPIO Int1 { get; }

        public void TriggerDataInterrupt()
        {
            if(true) //TODO fix, is data interrupt enabled?
            {
                Int1.Set(false);
                Int1.Set(true);
                Int1.Set(false);
                this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 1!");
            }
        }

        public void FeedPTSample(decimal pressure, decimal temperature, int repeat = 1)
        {

            var sampleP = new ScalarSample(pressure);
            var sampleT = new ScalarSample(temperature);
            for(var i = 0; i < repeat; i++)
            {
                fifoP.FeedSample(sampleP);
                fifoT.FeedSample(sampleT);
            }
        }

        public void FeedPTSample(string pathP, string pathT)
        {
            fifoP.FeedSamplesFromFile(pathP);
            fifoT.FeedSamplesFromFile(pathT);
        }

        private void DefineRegisters()
        {
            Registers.ChipId.Define(this, 0x50); //RO
            Registers.ErrReg.Define(this, 0x00); //RO
            Registers.Status.Define(this, 0x10); //RO NOTE wrong reset value, command decoder always ready in simulation?
            Registers.Data0.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[7:0]", valueProviderCallback: _ => PtoByte(fifoP.Sample.Value, 0)); //RO
            Registers.Data1.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[15:8]", valueProviderCallback: _ => PtoByte(fifoP.Sample.Value, 8)); //RO
            Registers.Data2.Define(this, 0x80)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[23:16]", valueProviderCallback: _ => PtoByte(fifoP.Sample.Value, 16)); //RO
            Registers.Data3.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[7:0]", valueProviderCallback: _ => TtoByte(fifoT.Sample.Value, 0)); //RO
            Registers.Data4.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[15:8]", valueProviderCallback: _ => TtoByte(fifoT.Sample.Value, 8)); //RO
            Registers.Data5.Define(this, 0x80)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[23:16]", valueProviderCallback: _ => TtoByte(fifoT.Sample.Value, 16)); //RO

            Registers.IntCtrl.Define(this, 0x02);
            Registers.IfConf.Define(this, 0x00);
            Registers.PwrCtrl.Define(this, 0x00);
            Registers.OSR.Define(this, 0x02) //RW
                .WithValueField(0, 3, name: "osr_p")
                .WithValueField(3, 3, name: "osr_t")
                .WithReservedBits(6, 2);
            Registers.ODR.Define(this, 0x00) //RW
                .WithValueField(0, 5, name: "odr_sel")
                .WithReservedBits(5, 3);
            Registers.Config.Define(this, 0x00) //RW
                .WithReservedBits(0, 1)
                .WithValueField(1, 3, name: "iir_filter")
                .WithReservedBits(4, 4);

            Registers.Cmd.Define(this, 0x00) //WO
                .WithWriteCallback((_, val) =>
                {
                    if(val == resetCommand)
                    {
                        Reset();
                    }
                });
        }

        private Registers registerAddress;
        private readonly SensorSamplesFifo<ScalarSample> fifoP;
        private readonly SensorSamplesFifo<ScalarSample> fifoT;

        // One bit: IFlagRegisterField
        // Multiple: IValueRegisterField

        private const byte resetCommand = 0xB6;

        // short←{⍵×16,384×2*Range}
        //TODO CHECK IF IN VALID RANGE!

        /*private byte mgToByte(decimal rawData, bool msb)
        {
            rawData = rawData * 32768 / ((decimal)(1000 * 1.5 * (2 << (short)accRange.Value)));
            short converted = (short)(rawData > 6.MaxValue ? 6.MaxValue : rawData < 6.MinValue ? 6.MinValue : rawData);
            return (byte)(converted >> (msb ? 8 : 0));
        }*/

        private byte PtoByte(decimal rawData, byte shift)
        {
            rawData = rawData; //FIXME
            int converted = (int)(rawData > 0xFFFFFF ? 0xFFFFFF : rawData);
            return (byte)(converted >> shift);
        }

        private byte TtoByte(decimal rawData, byte shift)
        {
            rawData = rawData; //FIXME
            int converted = (int)(rawData > 0xFFFFFF ? 0xFFFFFF : rawData);
            return (byte)(converted >> shift);
        }

        private enum Registers
        {
            ChipId = 0x00, // Read-Only
            // 0x01 reserved
            ErrReg = 0x02, // Read-Only
            Status = 0x03, // Read-Only
            Data0 = 0x04, // Read-Only
            Data1 = 0x05, // Read-Only
            Data2 = 0x06, // Read-Only
            Data3 = 0x07, // Read-Only
            Data4 = 0x08, // Read-Only
            Data5 = 0x09, // Read-Only
            // 0x0A - 0x0B reserved
            Sensortime0 = 0x0C, // Read-Only
            Sensortime1 = 0x0D, // Read-Only
            Sensortime2 = 0x0E, // Read-Only
            Event = 0x10, // Read-Only
            IntStatus = 0x11,  // Read-Only
            FIFOLength0 = 0x12, // Read-Only
            FIFOLength1 = 0x13, // Read-Only
            FIFOData = 0x14, // Read-Only
            FIFOWtm0 = 0x15, // Read-Write
            FIFOWtm1 = 0x16, // Read-Write
            FIFOConfig1 = 0x17, // Read-Write
            FIFOConfig2 = 0x18, // Read-Write
            IntCtrl = 0x19, // Read-Write
            IfConf = 0x1A, // Read-Write
            PwrCtrl = 0x1B, // Read-Write
            OSR = 0x1C, // Read-Write
            ODR = 0x1D, // Read-Write
            // 0x1E reserved
            Config = 0x1F, // Read-Write
            // 0x20 - 0x7D reserved
            Cmd = 0x7E // Read-Write
        }
    }
}
