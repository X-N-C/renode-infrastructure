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
    public class BMI088_ACCEL : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>, ISensor
    {
        public BMI088_ACCEL()
        {
            fifo = new SensorSamplesFifo<Vector3DSample>();
            RegistersCollection = new ByteRegisterCollection(this);
            Int1 = new GPIO();
            Int2 = new GPIO();
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
            Int1.Unset();
            Int2.Unset();
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

            if(data.Length > 1)
            {
                // skip the first byte as it contains register address
                // Must skip final byte, problem with I2C
                for(var i = 1; i < data.Length - 1; i++)
                {
                 this.Log(LogLevel.Noisy, "Writing 0x{0:X} to register {1} (0x{1:X})", data[i], registerAddress);
                 RegistersCollection.Write((byte)registerAddress, data[i]);
                }
            }
            else
            {
                this.Log(LogLevel.Noisy, "Preparing to read register {0} (0x{0:X})", registerAddress);
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
            if(registerAddress==Registers.RateXLSB)
            {
                fifo.TryDequeueNewSample();
            }
            //if registerAddress = 0x02 (xLSB) return 6 bytes (x,y,z)
            //else return 1 byte i.e. the register
            var result = new byte[registerAddress==Registers.RateXLSB?6:1];
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
        public GPIO Int2 { get; }

        public void TriggerDataInterrupt()
        {
            if(dataEn.Value)
            {
                if(int3Data.Value)
                {
                    Int1.Set(false);
                    Int1.Set(true);
                    Int1.Set(false);
                    this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 3!");
                }
                if(int4Data.Value)
                {
                    Int2.Set(false);
                    Int2.Set(true);
                    Int2.Set(false);
                    this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 4!");
                }
            }
        }

        public void FeedAccSample(decimal x, decimal y, decimal z, int repeat = 1)
        {

            var sample = new Vector3DSample(x, y, z);
            for(var i = 0; i < repeat; i++)
            {
                fifo.FeedSample(sample);
            }
        }

        public void FeedAccSample(string path)
        {
            fifo.FeedSamplesFromFile(path);
        }

        private void DefineRegisters()
        {
            Registers.AccChipID.Define(this, 0x1E); //RO
            Registers.AccXLSB.Define(this, 0x00);
                //.WithValueField(0, 8, FieldMode.Read, name: "RATE_X_LSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.X, false)); //RO
            Registers.AccXMSB.Define(this, 0x00);
                //.WithValueField(0, 8, FieldMode.Read, name: "RATE_X_MSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.X, true)); //RO
            Registers.AccYLSB.Define(this, 0x00);
                //.WithValueField(0, 8, FieldMode.Read, name: "RATE_Y_LSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Y, false)); //RO
            Registers.AccYMSB.Define(this, 0x00);
                //.WithValueField(0, 8, FieldMode.Read, name: "RATE_Y_MSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Y, true)); //RO
            Registers.AccZLSB.Define(this, 0x00);
                //.WithValueField(0, 8, FieldMode.Read, name: "RATE_Z_LSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Z, false)); //RO
            Registers.AccZMSB.Define(this, 0x00);
                //.WithValueField(0, 8, FieldMode.Read, name: "RATE_Z_MSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Z, true)); //RO

            Registers.AccSoftreset.Define(this, 0x00) //WO
                .WithWriteCallback((_, val) =>
                {
                    if(val == resetCommand)
                    {
                        Reset();
                    }
                });

        }

        private Registers registerAddress;
        private readonly SensorSamplesFifo<Vector3DSample> fifo;

        // One bit: IFlagRegisterField
        // Multiple: IValueRegisterField

        //private IValueRegisterField gyroRange;

        private IFlagRegisterField dataEn;
        private IFlagRegisterField fifoEn;
        private IFlagRegisterField int3Data;
        private IFlagRegisterField int3Fifo;
        private IFlagRegisterField int4Fifo;
        private IFlagRegisterField int4Data;

        private const byte resetCommand = 0xB6;

        // short←{⍵×16,384×2*Range}
        //TODO CHECK IF IN VALID RANGE!
        private byte DPStoByte(decimal rawData, bool msb)
        {
            rawData = rawData*(decimal)16.384*(1<<(short)gyroRange.Value);
            short converted = (short)(rawData > Int16.MaxValue ? Int16.MaxValue : rawData < Int16.MinValue ? Int16.MinValue : rawData);
            return (byte)(converted >> (msb ? 8 : 0));
        }

        private enum Registers
        {
            AccChipId = 0x00, // Read-Only
            // 0x01 reserved
            AccErrReg = 0x02, // Read-Only
            AccStatus = 0x03, // Read-Only
            // 0x04 - 0x11 reserved
            AccXLSB = 0x12, // Read-Only
            AccXMSB = 0x13, // Read-Only
            AccYLSB = 0x14, // Read-Only
            AccYMSB = 0x15, // Read-Only
            AccZLSB = 0x16, // Read-Only
            AccZMSB = 0x17, // Read-Only
            Sensortime0 = 0x18, // Read-Only
            Sensortime1 = 0x19, // Read-Only
            Sensortime2 = 0x1A,  // Read-Only
            // 0x1B - 0x1C reserved
            AccIntStat1 = 0x1D, // Read-Only
            // 0x1E - 0x21 reserved
            TempMSB = 0x22, // Read-Only
            TempLSB = 0x23, // Read-Only
            FIFOLength0 = 0x24, // Read-Only
            FIFOLength1 = 0x25, // Read-Only
            FIFOData = 0x26, // Read-Only
            // 0x27 - 0x3F reserved
            AccConf = 0x40, // Read-Write
            AccRange = 0x41, // Read-Write
            // 0x42 - 0x44 reserved
            FIFODowns = 0x45, // Read-Write
            FIFOWTM0 = 0x46, // Read-Write
            FIFOWTM1 = 0x47, // Read-Write
            FIFOConfig0 = 0x48, // Read-Write
            FIFOConfig1 = 0x49, // Read-Write
            // 0x4A - 0x52
            Int1IOCtrl = 0x53, // Read-Write
            Int2IOCtrl = 0x54, // Read-Write
            // 0x55 - 0x57 reserved
            IntMapData = 0x58, // Read-Write
            // 0x59 - 0x6C reserved
            AccSelfTest = 0x6D, // Read-Write
            // 0x6E - 0x7B reserved
            AccPwrConf = 0x7C, // Read-Write
            AccPwrCtrl = 0x7D, // Read-Write
            AccSoftreset = 0x7E // Write-Only
        }
    }
}
