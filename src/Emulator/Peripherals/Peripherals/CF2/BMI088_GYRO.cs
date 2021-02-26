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
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.CF2
{
    public class BMI088_GYRO : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>, ISensor
    {
        public BMI088_GYRO()
        {
            fifo = new SensorSamplesFifo<Vector3DSample>();
            RegistersCollection = new ByteRegisterCollection(this);
            Int3 = new GPIO();
            Int4 = new GPIO();
            irqs[0] = Int3;
            irqs[1] = Int4;
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
                /*foreach(var b in data.Skip(1))
                {
                    this.Log(LogLevel.Noisy, "Writing 0x{0:X} to register {1} (0x{1:X})", b, registerAddress);
                    RegistersCollection.Write((byte)registerAddress, b);
                }*/
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

        public byte[] Read(int count)
        {
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
                //registerAddress = (Registers)((int)registerAddress + 1);
            }
            return result;

        }

        public void FinishTransmission()
        {
        }

        public ByteRegisterCollection RegistersCollection { get; }
        public GPIO Int3 { get; }
        public GPIO Int4 { get; }

        //TODO Delete this, implement interrupt test function to be called in Renode
        public void testInterrupt()
        {
            Int3.Set(false);
            Int4.Set(false);
            Int3.Set(true);
            Int4.Set(true);
            this.Log(LogLevel.Error, "Interrupts set!");
        }

        public void TriggerDataInterrupt()
        {
            if(int3Data.Value)
            {
                Int3.Set(true);
                Int3.Set(false);
                this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 3!");
            }
            if(int4Data.Value)
            {
                Int4.Set(true);
                Int4.Set(false);
                this.Log(LogLevel.Noisy, "Data interrupt triggered on pin 4!");
            }
        }

        //TODO CHECK IF IN VALID RANGE!
        public void FeedGyroSample(decimal x, decimal y, decimal z)
        {
            var sample = new Vector3DSample(x, y, z);

            for(var i = 0; i < repeat; i++)
            {
                fifo.FeedSample(sample);
            }
            /*rateXLSB.Value = (DPStoByte(x, false));
            rateXMSB.Value = (DPStoByte(x, true));
            rateYLSB.Value = (DPStoByte(y, false));
            rateYMSB.Value = (DPStoByte(y, true));
            rateZLSB.Value = (DPStoByte(z, false));
            rateZMSB.Value = (DPStoByte(z, true));*/
            //Int3.Set(true);
            //Int4.Set(true);
        }

        private void DefineRegisters()
        {
            Registers.GyroChipID.Define(this, 0x0F); //RO
            Registers.RateXLSB.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "RATE_X_LSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.X, false)); //RO
                //.WithValueField(0, 8, out rateXLSB, name:"RATE_X_LSB"); //RO
            Registers.RateXMSB.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "RATE_X_MSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.X, true)); //RO
                //.WithValueField(0, 8, out rateXMSB, name:"RATE_X_MSB"); //RO
            Registers.RateYLSB.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "RATE_Y_LSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Y, false)); //RO
                //.WithValueField(0, 8, out rateYLSB, name:"RATE_Y_LSB"); //RO
            Registers.RateYMSB.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "RATE_Y_MSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Y, false)); //RO
                //.WithValueField(0, 8, out rateYMSB, name:"RATE_Y_MSB"); //RO
            Registers.RateZLSB.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "RATE_Z_LSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Z, false)); //RO
                //.WithValueField(0, 8, out rateZLSB, name:"RATE_Z_LSB"); //RO
            Registers.RateZMSB.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "RATE_Z_MSB", valueProviderCallback: _ => DPStoByte(fifo.Sample.Z, true)); //RO
                //.WithValueField(0, 8, out rateZMSB, name:"RATE_Z_MSB"); //RO

            Registers.GyroIntStat1.Define(this, 0x00)
                .WithReservedBits(0, 4)
                .WithFlag(4, name: "fifo_int")
                .WithReservedBits(5, 2)
                .WithFlag(7, name: "gyro_drdy"); //RO
            // FIFOSTATUS?
            Registers.GyroRange.Define(this, 0x00)
                .WithValueField(0, 8, out gyroRange, name:"GYRO_RANGE"); //RW
            Registers.GyroBandwidth.Define(this, 0x80); //RW
            Registers.GyroLPM1.Define(this, 0x00); //RW
            Registers.GyroSoftreset.Define(this, 0x00) //WO
                .WithWriteCallback((_, val) =>
                {
                    if(val == resetCommand)
                    {
                        Reset();
                    }
                });
            Registers.GyroIntCtrl.Define(this, 0x00)
                .WithReservedBits(0, 6)
                .WithFlag(6, name: "fifo_en")
                .WithFlag(7, name: "data_en");
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
         /*
            Registers.CtrlMeasurement.Define(this, 0x0) //RW
                .WithValueField(0, 5, out ctrlMeasurement , name: "CTRL_MEAS")
                .WithFlag(5, out startConversion, name: "SCO")
                .WithValueField(6, 2, out controlOversampling, name: "OSS")
                .WithWriteCallback((_, __) => HandleMeasurement());*/
        }

        private Registers registerAddress;
        private GPIO[] irqs = new GPIO[IrqAmount];
        private readonly SensorSamplesFifo<Vector3DSample> fifo;

        // One bit: IFlagRegisterField
        // Multiple: IValueRegisterField

        private IValueRegisterField gyroRange;
        private IValueRegisterField rateXLSB;
        private IValueRegisterField rateXMSB;
        private IValueRegisterField rateYLSB;
        private IValueRegisterField rateYMSB;
        private IValueRegisterField rateZLSB;
        private IValueRegisterField rateZMSB;

        private IFlagRegisterField dataEn;
        private IFlagRegisterField fifoEn;
        private IFlagRegisterField int3Data;
        private IFlagRegisterField int3Fifo;
        private IFlagRegisterField int4Fifo;
        private IFlagRegisterField int4Data;

        private const ushort IrqAmount = 2;
        private const byte resetCommand = 0xB6;

        // short←{⍵×16,384×2*Range}
        private byte DPStoByte(decimal rawData, bool msb)
        {
            short converted = (short)(rawData*(decimal)16.384*((short)1<<(short)gyroRange.Value));
            return (byte)(converted >> (msb?8:0));
        }

        private enum Registers
        {
            GyroChipID = 0x00, // Read-Only
            // 0x01 reserved
            RateXLSB = 0x02, // Read-Only
            RateXMSB = 0x03, // Read-Only
            RateYLSB = 0x04, // Read-Only
            RateYMSB = 0x05, // Read-Only
            RateZLSB = 0x06, // Read-Only
            RateZMSB = 0x07, // Read-Only
            // 0x08 - 0x09 reserved
            GyroIntStat1 = 0x0A, // Read-Only
            // 0x0B - 0x0D reserved
            FIFOStatus = 0x0E, // Read-Only
            GyroRange = 0x0F, // Read-Write
            GyroBandwidth = 0x10, // Read-Write
            GyroLPM1 = 0x11, // Read-Write
            // 0x12 - 0x13 reserved
            GyroSoftreset = 0x14, // Write-Only
            GyroIntCtrl = 0x15, // Read-Write
            Int3Int4IOConf = 0x16, // Read-Write
            // 0x17 reserved
            Int3Int4IOMap = 0x18, // Read-Write
            // 0x19 - 0x1D reserved
            FIFOWmEn = 0x1E, // Read-Write
            // 0x1F - 0x33 reseved
            FIFOExtIntS = 0x34, // Read-Write
            // 0x35 - 0x3B reserved
            GyroSelfTest = 0x3C,
            FIFOConfig0 = 0x3D, // Read-Write
            FIFOConfig1 = 0x3E, // Read-Write
            FIFOData = 0x3F // Read-Only
        }
    }
}
