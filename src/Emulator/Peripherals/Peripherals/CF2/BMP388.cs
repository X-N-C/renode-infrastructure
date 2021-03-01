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
            fifo = new SensorSamplesFifo<Vector3DSample>();
            RegistersCollection = new ByteRegisterCollection(this);
            Int3 = new GPIO();
            Int4 = new GPIO();
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
            Int3.Unset();
            Int4.Unset();
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
                 registerAddress++;
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
        public GPIO Int3 { get; }
        public GPIO Int4 { get; }

        public void TriggerDataInterrupt()
        {
            if(dataEn.Value)
            {
                if(int3Data.Value)
                {
                    Int3.Set(false);
                    Int3.Set(true);
                    Int3.Set(false);
                    this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 3!");
                }
                if(int4Data.Value)
                {
                    Int4.Set(false);
                    Int4.Set(true);
                    Int4.Set(false);
                    this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 4!");
                }
            }
        }

        public void FeedGyroSample(decimal x, decimal y, decimal z, int repeat = 1)
        {

            var sample = new Vector3DSample(x, y, z);
            for(var i = 0; i < repeat; i++)
            {
                fifo.FeedSample(sample);
            }
        }

        public void FeedGyroSample(string path)
        {
            fifo.FeedSamplesFromFile(path);
        }

        private void DefineRegisters()
        {
            Registers.ChipID.Define(this, 0x50); //RO
            Registers.ErrReg.Define(this, 0x00); //RO
            Registers.Status.Define(this, 0x00);
            Registers.Data0.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[7:0]", valueProviderCallback: _ => DPStoByte(fifo.Sample.X, false)); //RO
            Registers.Data1.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[15:8]", valueProviderCallback: _ => DPStoByte(fifo.Sample.X, true)); //RO
            Registers.Data2.Define(this, 0x80)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[23:16]", valueProviderCallback: _ => DPStoByte(fifo.Sample.Y, false)); //RO
            Registers.Data3.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[7:0]", valueProviderCallback: _ => DPStoByte(fifo.Sample.Y, true)); //RO
            Registers.Data4.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[15:8]", valueProviderCallback: _ => DPStoByte(fifo.Sample.Z, false)); //RO
            Registers.Data5.Define(this, 0x80)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[23:16]", valueProviderCallback: _ => DPStoByte(fifo.Sample.Z, true)); //RO

            Registers.IntCtrl.Define(this, 0x02);
            Registers.IfConf.Define(this, 0x00);
            Registers.PwrCtrl.Define(this, 0x00);
            Registers.OSR.Define(this, 0x02);
            Registers.ODR.Define(this, 0x00);
            Registers.Config.Define(this, 0x00);

            Registers.Cmd.Define(this, 0x00) //WO
                .WithWriteCallback((_, val) =>
                {
                    if(val == resetCommand)
                    {
                        Reset();
                    }
                });

           Registers.GyroIntStat1.Define(this, 0x00)
                .WithReservedBits(0, 4)
                .WithFlag(4, name: "fifo_int")
                .WithReservedBits(5, 2)
                .WithFlag(7, name: "gyro_drdy"); //RO
            // FIFOSTATUS?
            Registers.GyroRange.Define(this, 0x00)
                .WithValueField(0, 8, out gyroRange, name: "gyro_range"); //RW
            Registers.GyroBandwidth.Define(this, 0x80)
                .WithValueField(0, 8, name: "gyro_bw"); //RW //TODO should be used to determine output data rate
            Registers.GyroLPM1.Define(this, 0x00); //RW

            Registers.GyroIntCtrl.Define(this, 0x00)
                .WithReservedBits(0, 6)
                .WithFlag(6, out fifoEn, name: "fifo_en") // Currently unused
                .WithFlag(7, out dataEn, name: "data_en");
            Registers.Int3Int4IOConf.Define(this, 0x0F)
                .WithFlag(0, name: "int3_lvl")
                .WithFlag(1, name: "int3_od")
                .WithFlag(2, name: "int4_lvl")
                .WithFlag(3, name: "int4_od")
                .WithReservedBits(4, 4); // TODO implement?
            Registers.Int3Int4IOMap.Define(this, 0x00)
                .WithFlag(0, out int3Data, name: "int3_data")
                .WithReservedBits(1, 1)
                .WithFlag(2, out int3Fifo, name: "int3_fifo")
                .WithReservedBits(3, 2)
                .WithFlag(5, out int4Fifo, name: "int4_fifo")
                .WithReservedBits(6, 1)
                .WithFlag(7, out int4Data, name: "int4_data"); // data done
        }

        private Registers registerAddress;
        private readonly SensorSamplesFifo<Vector3DSample> fifo;

        // One bit: IFlagRegisterField
        // Multiple: IValueRegisterField

        private IValueRegisterField gyroRange;

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
