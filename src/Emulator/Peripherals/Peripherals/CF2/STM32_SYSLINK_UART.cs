//
// Copyright (c) 2010-2019 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using System.Collections.Generic;
using Antmicro.Migrant;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.UART;

namespace Antmicro.Renode.Peripherals.CF2
{
    [AllowedTranslations(AllowedTranslation.WordToDoubleWord | AllowedTranslation.ByteToDoubleWord)]
    public class STM32_SYSLINK_UART : BasicDoubleWordPeripheral, IUART
    {
        public STM32_SYSLINK_UART(Machine machine, uint frequency = 8000000) : base(machine)
        {
            this.frequency = frequency;
            DefineRegisters();
            this.DataLength = UInt32.MaxValue - 6; // Packet lengths have data and 6 extra bytes
        }

        public void WriteChar(byte value)
        {
            // Read entire message
            // With the queue, read byte 2 (0-indexed) to find message type
            // Sends back the correct message once the entire package has been received
            receiveFifo.Enqueue(value);
            if(receiveFifo.Count == 4)
            {
                DataLength = value;
            }
            if(receiveFifo.Count == DataLength + 6)
            {
                DataLength = UInt32.MaxValue - 6;
                SendBack();
            }
        }

        private uint DataLength;

        private void SendBack()
        {
            byte[] data = receiveFifo.ToArray();
            switch(data[2])
            {
                case 0x20: // SYSLINK_OW_SCAN
                    byte[] OwScanData = CreateMessage(0x20, 0x01, new byte[]{0x00});
                    for(int i = 0; i < OwScanData.Length; ++i)
                    {
                        CharReceived?.Invoke((byte)OwScanData[i]);
                    }
                    receiveFifo.Clear();
                    break;
                default:
                    while(receiveFifo.Count > 0)
                    {
                        CharReceived?.Invoke((byte)receiveFifo.Dequeue());
                    }
                    break;
            }

            this.Log(LogLevel.Noisy, "Complete data sent back!");
        }

        // Creates the message to be sent back
        public byte[] CreateMessage(byte command, byte length, byte[] data)
        {
            byte[] result = new byte[length+6];
            result[0] = 0xBC;
            result[1] = 0xCF;
            result[2] = command;
            result[3] = length;
            for(int i = 0; i < length; i++)
            {
                result[4+i] = data[i];
            }
            for(int i = 2; i < length+4; i++)
            {
                result[length+4] += result[i]; // Checksum 1
                result[length+5] += result[length+4]; // Checksum 2
            }

            return result;
        }

        public override void Reset()
        {
            base.Reset();
            receiveFifo.Clear();
        }

        public uint BaudRate
        {
            get
            {
                //OversamplingMode.By8 means we ignore the oldest bit of dividerFraction.Value
                var fraction = oversamplingMode.Value == OversamplingMode.By16 ? dividerFraction.Value : dividerFraction.Value & 0b111;

                var divisor = 8 * (2 - (int)oversamplingMode.Value) * (dividerMantissa.Value + fraction / 16.0);
                return divisor == 0 ? 0 : (uint)(frequency / divisor);
            }
        }

        public Bits StopBits
        {
            get
            {
                switch(stopBits.Value)
                {
                case StopBitsValues.Half:
                    return Bits.Half;
                case StopBitsValues.One:
                    return Bits.One;
                case StopBitsValues.OneAndAHalf:
                    return Bits.OneAndAHalf;
                case StopBitsValues.Two:
                    return Bits.Two;
                default:
                    throw new ArgumentException("Invalid stop bits value");
                }
            }
        }

        public Parity ParityBit => parityControlEnabled.Value ?
                                    (paritySelection.Value == ParitySelection.Even ?
                                        Parity.Even :
                                        Parity.Odd) :
                                    Parity.None;

        public GPIO IRQ { get; } = new GPIO();

        public event Action<byte> CharReceived;

        private void DefineRegisters()
        {
            Register.Status.Define(this, 0xC0, "USART_SR")
                .WithTaggedFlag("PE", 0)
                .WithTaggedFlag("FE", 1)
                .WithTaggedFlag("NF", 2)
                .WithFlag(3, FieldMode.Read, valueProviderCallback: _ => false, name: "ORE") // we assume no receive overruns
                .WithTaggedFlag("IDLE", 4)
                .WithFlag(5, out readFifoNotEmpty, FieldMode.Read | FieldMode.WriteZeroToClear, name: "RXNE") // as these two flags are WZTC, we cannot just calculate their results
                .WithFlag(6, out transmissionComplete, FieldMode.Read | FieldMode.WriteZeroToClear, name: "TC")
                .WithFlag(7, FieldMode.Read, valueProviderCallback: _ => true, name: "TXE") // we always assume "transmit data register empty"
                .WithTaggedFlag("LBD", 8)
                .WithTaggedFlag("CTS", 9)
                .WithReservedBits(10, 22)
                .WithWriteCallback((_, __) => Update())
            ;
            Register.Data.Define(this, name: "USART_DR")
                .WithValueField(0, 9, valueProviderCallback: _ =>
                    {
                        uint value = 0;
                        if(receiveFifo.Count > 0)
                        {
                            value = receiveFifo.Dequeue();
                        }
                        readFifoNotEmpty.Value = receiveFifo.Count > 0;
                        Update();
                        return value;
                    }, writeCallback: (_, value) =>
                    {
                        if(!usartEnabled.Value && !transmitterEnabled.Value)
                        {
                            this.Log(LogLevel.Warning, "Trying to transmit a character, but the transmitter is not enabled. dropping.");
                            return;
                        }
                        CharReceived?.Invoke((byte)value);
                        transmissionComplete.Value = true;
                        Update();
                    }, name: "DR"
                )
            ;
            Register.BaudRate.Define(this, name: "USART_BRR")
                .WithValueField(0, 4, out dividerFraction, name: "DIV_Fraction")
                .WithValueField(4, 12, out dividerMantissa, name: "DIV_Mantissa")
            ;
            Register.Control1.Define(this, name: "USART_CR1")
                .WithTaggedFlag("SBK", 0)
                .WithTaggedFlag("RWU", 1)
                .WithFlag(2, out receiverEnabled, name: "RE")
                .WithFlag(3, out transmitterEnabled, name: "TE")
                .WithTaggedFlag("IDLEIE", 4)
                .WithFlag(5, out receiverNotEmptyInterruptEnabled, name: "RXNEIE")
                .WithFlag(6, out transmissionCompleteInterruptEnabled, name: "TCIE")
                .WithFlag(7, out transmitDataRegisterEmptyInterruptEnabled, name: "TXEIE")
                .WithTaggedFlag("PEIE", 8)
                .WithEnumField(9, 1, out paritySelection, name: "PS")
                .WithFlag(10, out parityControlEnabled, name: "PCE")
                .WithTaggedFlag("WAKE", 11)
                .WithTaggedFlag("M", 12)
                .WithFlag(13, out usartEnabled, name: "UE")
                .WithReservedBits(14, 1)
                .WithEnumField(15, 1, out oversamplingMode, name: "OVER8")
                .WithReservedBits(16, 16)
                .WithWriteCallback((_, __) => Update())
            ;
            Register.Control2.Define(this, name: "USART_CR2")
                .WithTag("ADD", 0, 4)
                .WithReservedBits(5, 1)
                .WithTaggedFlag("LBDIE", 6)
                .WithReservedBits(7, 1)
                .WithTaggedFlag("LBCL", 8)
                .WithTaggedFlag("CPHA", 9)
                .WithTaggedFlag("CPOL", 10)
                .WithTaggedFlag("CLKEN", 11)
                .WithEnumField(12, 2, out stopBits, name: "STOP")
                .WithTaggedFlag("LINEN", 14)
                .WithReservedBits(15, 17)
            ;
        }

        private void Update()
        {
            IRQ.Set(
                (receiverNotEmptyInterruptEnabled.Value && readFifoNotEmpty.Value) ||
                (transmitDataRegisterEmptyInterruptEnabled.Value) || // TXE is assumed to be true
                (transmissionCompleteInterruptEnabled.Value && transmissionComplete.Value)
            );
        }

        private readonly uint frequency;

        private IEnumRegisterField<OversamplingMode> oversamplingMode;
        private IEnumRegisterField<StopBitsValues> stopBits;
        private IFlagRegisterField usartEnabled;
        private IFlagRegisterField parityControlEnabled;
        private IEnumRegisterField<ParitySelection> paritySelection;
        private IFlagRegisterField transmissionCompleteInterruptEnabled;
        private IFlagRegisterField transmitDataRegisterEmptyInterruptEnabled;
        private IFlagRegisterField receiverNotEmptyInterruptEnabled;
        private IFlagRegisterField receiverEnabled;
        private IFlagRegisterField transmitterEnabled;
        private IFlagRegisterField readFifoNotEmpty;
        private IFlagRegisterField transmissionComplete;
        private IValueRegisterField dividerMantissa;
        private IValueRegisterField dividerFraction;

        private readonly Queue<byte> receiveFifo = new Queue<byte>();

        private enum OversamplingMode
        {
            By16 = 0,
            By8 = 1
        }

        private enum StopBitsValues
        {
            One = 0,
            Half = 1,
            Two = 2,
            OneAndAHalf = 3
        }

        private enum ParitySelection
        {
            Even = 0,
            Odd = 1
        }

        private enum Register : long
        {
            Status = 0x00,
            Data = 0x04,
            BaudRate = 0x08,
            Control1 = 0x0C,
            Control2 = 0x10,
            Control3 = 0x14,
            GuardTimeAndPrescaler = 0x18
        }
    }
}
