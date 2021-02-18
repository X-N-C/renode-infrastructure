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
            Array.Copy(configdata, storage, configdata.Length);
        }

        public byte[] Read(int count)
        {
            //var result = outputBuffer.ToArray();
            //this.Log(LogLevel.Noisy, "Reading {0} bytes from the device (asked for {1} bytes).", result.Length, count);
            //outputBuffer.Clear();
            this.Log(LogLevel.Noisy, "Reading 0x{0:X} bytes from EEPROM",count);
            byte[] result = {storage[(highAddress<<8)+lowAddress]};
            lowAddress++;
            return result;
            //return storage;
            //return new Byte[1];
        }

        public void Write(byte[] packet)
        {
         // Two first bytes written are addresses first high then low
         // switch on data length, 2, 3 and more or default (0 and 1 causes error?)
         // calculate eventual page overflow
         // copy over input data to storage

         // less than 2 ERROR more than 32 varning
            this.Log(LogLevel.Error, "In Write! packet length: {0}", packet.Length);
            if(packet.Length < 2)
            {
                this.Log(LogLevel.Error, "Tried to write less than two bytes, i.e. missing address bytes.");
                return;
            }
            if(packet.Length > 34)
            {
                this.Log(LogLevel.Warning, "Page overflow. Trying to write {0} bytes of data and new data will be overwritten!", packet.Length-2);
            }
            if((packet[0] & ~0x1F) != 0)
            {
                this.Log(LogLevel.Warning, "Unused bits: 0x{0:X} for high address ignored!", packet[0] & ~0x1F);
            }
            highAddress = (ushort)(packet[0] & 0x1F);
            lowAddress = packet[1];
            byte inPageAddress = (byte)(lowAddress & 0x1F);
            ushort pageAddress = (ushort)((highAddress << 8) + (lowAddress & 0xE0));
            this.Log(LogLevel.Error, "inPageAddress: 0x{0:X} pageAddress: 0x{1:X}",inPageAddress, pageAddress);
            for(int i = 0; i < packet.Length - 2; ++i)
            {
                storage[pageAddress + (inPageAddress+i)%0x20] = packet[i+2];
                this.Log(LogLevel.Noisy, "Trying to write data byte {0} (0x{1:X})", i, packet[i+2]);
            }
            if(packet.Length > 3)
            {
                // Handle the write delay!
            }
             // vilken data ska kopieras över?
             // var ska den hamna?
             // är page overflow ett problem?
             // flytta runt i en buffer
             // kopiera till storage
             // delay?
        }

        public void FinishTransmission()
        {
           this.Log(LogLevel.Noisy, "FinishTransmission in EEPROM!");
        }

        public void Reset()
        {
        }

        public void testWrite(int option)
        {
            switch(option)
            {
            case 0:
                Write(new byte[]{0x00});
                break;
            case 1:
                Write(new Byte[]{0x01,0x01});
                break;
            case 2:
                Write(new Byte[]{0x00,0x00,0xBC,0xCF});
                break;
            case 3:
                Write(new byte[40]);
                break;
            case 4:
                Write(new Byte[]{0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7, 0xE7, 0xE7, 0xE7, 0xE7, 0x04, 0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7, 0xE7, 0xE7, 0xE7, 0xE7, 0x04});
                break;
            case 5:
                Write(new Byte[]{0x36,0x10,0xBC,0xCF});
                break;
            }
            return;
        }
        //private byte[] data = {0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE8, 0xE9, 0xEA, 0xEB, 0xEC, 0x12};
        private byte[] configdata = {0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7, 0xE7, 0xE7, 0xE7, 0xE7, 0x04, 0xBC, 0xCF}; // Wrong checksum
        private byte[] storage = new byte[8192];
        private ushort highAddress; // Masked with 0x1F?
        private byte lowAddress;
        //[highAddress<<8+lowAddress] ?
        //private byte[] data = {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44};

        /*
        For the delay:
        double timestampWriteFinished = machine.ElapsedVirtualTime.TimeElapsed.TotalMilliseconds



        */
    }
}
