﻿//
// Copyright (c) 2010-2020 Antmicro
//
//  This file is licensed under the MIT License.
//  Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class K6xF_UART : UARTBase, IBytePeripheral, IKnownSize
    {
        public K6xF_UART(Machine machine) : base(machine)
        {
            baudRateDivValue = 0;
            transmitQueue = new Queue<byte>();

            var registersMap = new Dictionary<long, ByteRegister>
            {
                {(long)Registers.BaudRateHigh, new ByteRegister(this)
                    .WithTaggedFlag("LBKDIE", 7)
                    .WithTaggedFlag("RXEDGIE", 6)
                    .WithTaggedFlag("SBNS", 5)
                    .WithValueField(0, 5, FieldMode.Write | FieldMode.Read, writeCallback: (_, value) =>
                    {
                        // setting the high bits of the baud rate factor
                        var b_mask = 0x1f00u;
                        var b_rate_value = (baudRateDivValue & ~b_mask) | ( value << 8);
                        baudRateDivValue = b_rate_value;
                    },name: "SBR")
                },
                {(long)Registers.BaudRateLow, new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Write | FieldMode.Read, writeCallback: (_, value) =>
                    {
                        // setting the low bits of the baud rate factor
                        var b_mask = 0xffu;
                        var b_rate_value = (baudRateDivValue & ~b_mask) | value;
                        baudRateDivValue = b_rate_value;
                    },name: "SBR")
                },
                {(long)Registers.Control1, new ByteRegister(this)},
                {(long)Registers.Control2, new ByteRegister(this)
                    .WithFlag(7, out transmitterIRQEnabled, FieldMode.Write | FieldMode.Read, name: "TIE")
                    .WithTaggedFlag("TCIE", 6)
                    .WithFlag(5, out receiverIRQEnabled, FieldMode.Write | FieldMode.Read, name: "RIE")
                    .WithTaggedFlag("ILIE", 4)
                    .WithFlag(3, out transmitterEnabled, FieldMode.Write | FieldMode.Read, name: "TE")
                    .WithFlag(2, out receiverEnabled, FieldMode.Write | FieldMode.Read, name: "RE")
                    .WithTaggedFlag("RWU", 1)
                    .WithTaggedFlag("SBK", 0)
                    .WithWriteCallback((_, __) =>
                    {
                        UpdateInterrupts();
                    })
                },
                {(long)Registers.Status1, new ByteRegister(this)
                    .WithFlag(7, FieldMode.Read, valueProviderCallback: _ =>
                    {
                        return transmitQueue.Count <= transmitWatermark;
                    },name: "TDRE")
                    .WithTaggedFlag("TC", 6)
                    .WithFlag(5, FieldMode.Read, valueProviderCallback: _ =>
                    {
                        return Count >= receiverWatermark;
                    }, name: "RDRF")
                    .WithTaggedFlag("IDLE", 4)
                    .WithTaggedFlag("OR", 3)
                    .WithTaggedFlag("NF", 2)
                    .WithTaggedFlag("FE", 1)
                },
                {(long)Registers.Status2, new ByteRegister(this)
                    .WithTaggedFlag("LBKDIF", 7)
                    .WithTaggedFlag("RXEDGIF", 6)
                    .WithTaggedFlag("MSBF", 5)
                    .WithTaggedFlag("RXINV", 4)
                    .WithTaggedFlag("RWUID", 3)
                    .WithTaggedFlag("BRK13", 2)
                    .WithTaggedFlag("LBKDE", 1)
                    .WithTaggedFlag("RAF", 0)
                },
                {(long)Registers.Control3, new ByteRegister(this)},
                {(long)Registers.Data, new ByteRegister(this)
                   .WithValueField(0, 8, FieldMode.Write | FieldMode.Read, 
                    writeCallback: (_, b) =>
                    {
                        if(!transmitterEnabled.Value)
                        {
                            this.Log(LogLevel.Warning, "Transmitter not enabled");
                            return;
                        }
                        transmitQueue.Enqueue((byte)b);
                        TransmitData();
                        UpdateInterrupts();
                    },
                    valueProviderCallback: _ =>
                    {
                        if (!receiverEnabled.Value)
                        {
                            return 0;
                        }
                        if(!TryGetCharacter(out var character))
                        {
                            this.Log(LogLevel.Warning, "Trying to read data from empty receive fifo");
                        }
                        UpdateInterrupts();
                        return character;
                    },
                    name: "RT")
                },
                {(long)Registers.MatchAddress1, new ByteRegister(this)},
                {(long)Registers.MatchAddress2, new ByteRegister(this)},
                {(long)Registers.Control4, new ByteRegister(this)
                    .WithTaggedFlag("MAEN1", 7)
                    .WithTaggedFlag("MAEN2", 6)
                    .WithTaggedFlag("M10", 5)
                    .WithValueField(0, 5, out baudRateFineAdjustValue, name: "BRFA")
                },
                {(long)Registers.Control5, new ByteRegister(this)},
                {(long)Registers.ExtendedData, new ByteRegister(this)
                    .WithTaggedFlag("NOISY", 7)
                    .WithTaggedFlag("PARITYE", 6)
                    .WithReservedBits(0,6)
                },
                {(long)Registers.Modem, new ByteRegister(this)},
                {(long)Registers.Infrared, new ByteRegister(this)},
                {(long)Registers.FIFOParameters, new ByteRegister(this)},
                {(long)Registers.FIFOControl, new ByteRegister(this)},
                {(long)Registers.FIFOStatus, new ByteRegister(this)
                    .WithTaggedFlag("TXEMPT", 7)
                    .WithTaggedFlag("RXEMPT", 6)
                    .WithReservedBits(3,3)
                    .WithTaggedFlag("RXOF", 2)
                    .WithTaggedFlag("TXOF", 1)
                    .WithTaggedFlag("RXUF", 0)
                },
                {(long)Registers.FIFOTransmitWatermark, new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Write | FieldMode.Read,
                    writeCallback: (_, b) =>
                    {
                        if(transmitterEnabled.Value)
                        {
                            this.Log(LogLevel.Warning, "Cannot set transmitter watermark when transmitter is enabled");
                            return;
                        }
                        transmitWatermark = b;
                        UpdateInterrupts();
                    },
                    valueProviderCallback: _ =>
                    {
                        return transmitWatermark;
                    },
                    name: "TXWATER")
                },
                {(long)Registers.FIFOTransmitCount, new ByteRegister(this)},
                {(long)Registers.FIFOReceiveWatermark, new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Write | FieldMode.Read,
                    writeCallback: (_, b) =>
                    {
                        if(receiverEnabled.Value)
                        {
                            this.Log(LogLevel.Warning, "Cannot set receiver watermark when receiver is enabled");
                            return;
                        }
                        receiverWatermark = b;
                        UpdateInterrupts();
                    },
                    valueProviderCallback: _ =>
                    {
                        return receiverWatermark;
                    },
                    name: "RXWATER")
                },
                {(long)Registers.FIFOReceiveCount, new ByteRegister(this)
                    .WithValueField(0,8, FieldMode.Read, valueProviderCallback: _ =>
                    {
                        return (uint)Count;
                    },name: "RXCOUNT")
                },
                {(long)Registers.Control7816, new ByteRegister(this)},
                {(long)Registers.InterruptEnable7816, new ByteRegister(this)},
                {(long)Registers.InterruptStatus7816, new ByteRegister(this)},
                {(long)Registers.WaitParameter7816, new ByteRegister(this)},
                {(long)Registers.WaitN7816, new ByteRegister(this)},
                {(long)Registers.WaitFD7816, new ByteRegister(this)},
                {(long)Registers.ErrorThreshold, new ByteRegister(this)},
                {(long)Registers.TransmitLength, new ByteRegister(this)}
            };

            IRQ = new GPIO();
            registers = new ByteRegisterCollection(this, registersMap);
        }

        byte IBytePeripheral.ReadByte(long offset)
        {
            lock(innerLock)
            {
                var value = registers.Read(offset);
                return value;
            }
        }

        void IBytePeripheral.WriteByte(long offset, byte value)
        {
            lock(innerLock)
            {
                registers.Write(offset, value);
            }
        }

        protected override void CharWritten()
        {
            UpdateInterrupts();
        }

        protected override void QueueEmptied()
        {
            this.Log(LogLevel.Debug, "Queue emptied");
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; private set; }

        //TODO should be calculated based upon UART clock
        public override uint BaudRate => 115200;

        private void TransmitData()
        {
            if(transmitQueue.Count < transmitWatermark)
                return;

            while (transmitQueue.Count != 0)
            {
                var b = transmitQueue.Dequeue();
                this.TransmitCharacter((byte)b);
            }
        }

        public override Bits StopBits 
        {
            get
            {
                this.Log(LogLevel.Debug, "Requesting StopBits");
                return Bits.One;
            }
        }

        public override Parity ParityBit
        {
            get
            {
                this.Log(LogLevel.Debug, "Requesting Parity");
                return Parity.Even;
            }
        }

        private void UpdateInterrupts()
        {
            lock(innerLock)
            {
                IRQ.Set( (transmitterEnabled.Value && transmitterIRQEnabled.Value ) || 
                         (receiverEnabled.Value && receiverIRQEnabled.Value && Count >= receiverWatermark));
            }
        }


        private readonly ByteRegisterCollection registers;

        private uint baudRateDivValue;
        private IValueRegisterField baudRateFineAdjustValue;

        private IFlagRegisterField receiverEnabled;
        private IFlagRegisterField transmitterEnabled;
        private uint receiverWatermark = 0;
        private uint transmitWatermark = 0;

        private IFlagRegisterField transmitterIRQEnabled;
        private IFlagRegisterField receiverIRQEnabled;

        private readonly Queue<byte> transmitQueue;

        private enum Registers
        {
            BaudRateHigh = 0x00,
            BaudRateLow = 0x01,
            Control1 = 0x02,
            Control2 = 0x03,
            Status1 = 0x04,
            Status2 = 0x05,
            Control3 = 0x06,
            Data = 0x07,
            MatchAddress1 = 0x08,
            MatchAddress2 = 0x09,
            Control4 = 0x0A,
            Control5 = 0x0B,
            ExtendedData = 0x0C,
            Modem = 0x0D,
            Infrared = 0x0E,
            FIFOParameters = 0x10,
            FIFOControl = 0x11,
            FIFOStatus = 0x12,
            FIFOTransmitWatermark = 0x13,
            FIFOTransmitCount = 0x14,
            FIFOReceiveWatermark = 0x15,
            FIFOReceiveCount = 0x16,
            Control7816 = 0x18,
            InterruptEnable7816 = 0x19,
            InterruptStatus7816 = 0x1A,
            WaitParameter7816 = 0x1B,
            WaitN7816 = 0x1C,
            WaitFD7816 = 0x1D,
            ErrorThreshold = 0x1E,
            TransmitLength = 0x1F
        }

        private enum TransmitCompleteFlagValues
        {
            Active = 0,
            Idle = 1
        }

    }
}