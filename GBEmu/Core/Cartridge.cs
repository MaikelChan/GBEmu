﻿using GBEmu.Core.MemoryBankControllers;
using GBEmu.Core.State;
using System;
using System.IO;
using System.Text;

namespace GBEmu.Core
{
    class Cartridge : IDisposable, IState
    {
        readonly byte[] rom;
        readonly byte[] ram;
        readonly IMemoryBankController mbc;
        readonly Action<byte[]> saveUpdateCallback;

        public string Title { get; }

        public bool IsRamEnabled
        {
            get
            {
                if (ram != null) return isRamEnabled;
                return false;
            }

            set
            {
                if (ram != null)
                {
                    if (isRamEnabled && !value) saveUpdateCallback?.Invoke(ram);
                    isRamEnabled = value;
                }
            }
        }

        public int RomSize
        {
            get
            {
                switch (rom[0x148])
                {
                    case >= 0 and < 9: return 32768 << rom[0x148];
                    default: throw new NotImplementedException($"ROM of size ID; 0x{rom[0x148]:X2} is not implemented");
                }
            }
        }

        public int RamSize
        {
            get
            {
                switch (rom[0x149])
                {
                    case 0: return 0;
                    case 1: throw new InvalidDataException($"Cartridge with MBC1 and RAM with ID: 0x{rom[0x149]:X2} shouldn't be valid"); // return 1024 * 2;
                    case 2: return 1024 * 8;
                    case 3: return 1024 * 32;
                    case 4: return 1024 * 128;
                    case 5: return 1024 * 64;
                    default: throw new NotImplementedException($"Cartridge with MBC1 and RAM with ID: 0x{rom[0x149]:X2} is not implemented");
                }
            }
        }

        bool isRamEnabled;

        public const ushort ROM_BANK_START_ADDRESS = 0x0000;
        public const ushort ROM_BANK_END_ADDRESS = 0x4000;

        public const ushort SWITCHABLE_ROM_BANK_START_ADDRESS = 0x4000;
        public const ushort SWITCHABLE_ROM_BANK_END_ADDRESS = 0x8000;

        public const ushort EXTERNAL_RAM_BANK_START_ADDRESS = 0xA000;
        public const ushort EXTERNAL_RAM_BANK_END_ADDRESS = 0xC000;

        public Cartridge(byte[] romData, byte[] saveData, Action<byte[]> saveUpdateCallback)
        {
            rom = romData;
            this.saveUpdateCallback = saveUpdateCallback;

            Title = Encoding.ASCII.GetString(romData, 0x134, 0xF).TrimEnd('\0');

            switch (romData[0x147])
            {
                case 0x0:
                    mbc = new NullMBC(romData);
                    break;

                case 0x1:
                case 0x2:
                case 0x3:
                    ram = GetInitializedRam(saveData);
                    mbc = new MBC1(this, romData, ram);
                    break;

                case 0x11:
                case 0x12:
                case 0x13:
                    ram = GetInitializedRam(saveData);
                    mbc = new MBC3(this, romData, ram);
                    break;

                case 0x19:
                case 0x1A:
                case 0x1B:
                case 0x1C:
                case 0x1D:
                case 0x1E:
                    ram = GetInitializedRam(saveData);
                    mbc = new MBC5(this, romData, ram);
                    break;

                default:
                    throw new NotImplementedException($"MBC with ID: 0x{romData[0x147]:X4} is not implemented");
            }

            int romSize = RomSize;
            if (romData.Length != romSize) throw new InvalidDataException($"The ROM is expected to be {romSize} bytes but is {romData.Length} bytes");
        }

        public byte this[int index]
        {
            get => mbc[index];
            set => mbc[index] = value;
        }

        public void Dispose()
        {
            if (ram != null)
            {
                saveUpdateCallback?.Invoke(ram);
            }
        }

        byte[] GetInitializedRam(byte[] saveData)
        {
            int ramSize = RamSize;

            if (ramSize == 0)
            {
                return null;
            }
            else
            {
                if (saveData == null)
                {
                    return new byte[ramSize];
                }
                else
                {
                    if (saveData.Length != ramSize)
                    {
                        Utils.Log(LogType.Warning, $"Save data is {saveData.Length} bytes of size but the game expects {ramSize} bytes.");
                        return new byte[ramSize];
                    }
                    else
                    {
                        return saveData;
                    }
                }
            }
        }

        public void GetState(SaveState state)
        {
            if (ram != null) Array.Copy(ram, state.ExternalRam, RamSize);
            state.IsExternalRamEnabled = isRamEnabled;
            state.MemoryBankControllerState = mbc.GetState();
        }

        public void SetState(SaveState state)
        {
            if (ram != null) Array.Copy(state.ExternalRam, ram, RamSize);
            isRamEnabled = state.IsExternalRamEnabled;
            mbc.SetState(state.MemoryBankControllerState);
        }
    }
}