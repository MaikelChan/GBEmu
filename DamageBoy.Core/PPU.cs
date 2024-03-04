﻿using DamageBoy.Core.State;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using static DamageBoy.Core.GameBoy;

namespace DamageBoy.Core;

class PPU : IDisposable, IState
{
    readonly GameBoy gameBoy;
    readonly InterruptHandler interruptHandler;
    readonly VRAM vram;
    readonly OAM oam;
    readonly DMA dma;
    readonly ScreenUpdateDelegate screenUpdateCallback;
    readonly FinishedVBlankDelegate finishedVBlankCallback;

    readonly ushort[][] lcdPixelBuffers;
    readonly List<byte> spriteIndicesInCurrentLine;

    // LCD Control

    bool lcdDisplayEnable;
    public bool LCDDisplayEnable
    {
        get { return lcdDisplayEnable; }
        set
        {
            if (lcdDisplayEnable && !value) ClearScreen();
            lcdDisplayEnable = value;
        }
    }
    public bool WindowTileMapDisplaySelect { get; set; }
    public bool WindowDisplayEnable { get; set; }
    public bool BGAndWindowTileDataSelect { get; set; }
    public bool BGTileMapDisplaySelect { get; set; }
    public bool OBJSize { get; set; }
    public bool OBJDisplayEnable { get; set; }
    public bool BGDisplayEnable { get; set; }

    // LCD Status

    public Modes LCDStatusMode { get; set; }
    public CoincidenceFlagModes LCDStatusCoincidenceFlag { get; set; }
    public bool LCDStatusHorizontalBlankInterrupt { get; set; }
    public bool LCDStatusVerticalBlankInterrupt { get; set; }
    public bool LCDStatusOAMSearchInterrupt { get; set; }
    public bool LCDStatusCoincidenceInterrupt { get; set; }

    // LCD Position and Scrolling

    public byte ScrollY { get; set; }
    public byte ScrollX { get; set; }
    public byte LY { get; set; }
    public byte LYC { get; set; }
    public byte WindowY { get; set; }
    public byte WindowX { get; set; }

    // LCD Monochrome Palettes

    public byte BackgroundPalette { get; set; }
    public byte ObjectPalette0 { get; set; }
    public byte ObjectPalette1 { get; set; }

    // Color Palettes

    public ushort ColorBgPaletteAddress { get; set; }
    public bool ColorBgPaletteAutoIncrement { get; set; }
    public byte ColorBgPalette
    {
        get
        {
            // if (CanCPUAccessVRAM())  TODO: Timing problems seem to sometimes not set the colors correctly 
            return colorBgPalette[ColorBgPaletteAddress];
            //return 0xFF;
        }
        set
        {
            // if (CanCPUAccessVRAM())  TODO: Timing problems seem to sometimes not set the colors correctly 
            colorBgPalette[ColorBgPaletteAddress] = value;
            if (ColorBgPaletteAutoIncrement) ColorBgPaletteAddress = (ushort)((ColorBgPaletteAddress + 1) & 0b0011_1111);
        }
    }

    public ushort ColorObjPaletteAddress { get; set; }
    public bool ColorObjPaletteAutoIncrement { get; set; }
    public byte ColorObjPalette
    {
        get
        {
            // if (CanCPUAccessVRAM())  TODO: Timing problems seem to sometimes not set the colors correctly 
            return colorObjPalette[ColorObjPaletteAddress];
            //return 0xFF;
        }
        set
        {
            // if (CanCPUAccessVRAM())  TODO: Timing problems seem to sometimes not set the colors correctly 
            colorObjPalette[ColorObjPaletteAddress] = value;
            if (ColorObjPaletteAutoIncrement) ColorObjPaletteAddress = (ushort)((ColorObjPaletteAddress + 1) & 0b0011_1111);
        }
    }

    readonly byte[] colorBgPalette;
    readonly byte[] colorObjPalette;

    // Constants

    const byte BG_TILES_X = 32;
    //const byte BG_TILES_Y = 32;
    //const byte LCD_TILES_X = RES_X >> 3;
    //const byte LCD_TILES_Y = RES_Y >> 3;
    const byte TILE_BYTES_SIZE = 16;

    const byte MAX_SPRITES = 40;
    const byte MAX_SPRITES_PER_LINE = 10;
    const byte OAM_ENTRY_SIZE = 4;
    const byte SPRITE_WIDTH = 8;
    const byte SPRITE_HEIGHT = 8;
    const byte SPRITE_MAX_HEIGHT = 16;

    const int OAM_SEARCH_CLOCKS = 80;
    const int PIXEL_TRANSFER_CLOCKS = 172;
    const int HORIZONTAL_BLANK_CLOCKS = 204;
    const int VERTICAL_BLANK_CLOCKS = OAM_SEARCH_CLOCKS + PIXEL_TRANSFER_CLOCKS + HORIZONTAL_BLANK_CLOCKS;
    const int VERTICAL_BLANK_LINES = 10;
    public const int SCREEN_CLOCKS = VERTICAL_BLANK_CLOCKS * (Constants.RES_Y + VERTICAL_BLANK_LINES);

    //R5G5B5A1
    const ushort COLOR_BLACK = 0x0001;
    const ushort COLOR_DARK_GRAY = 0x5295;
    const ushort COLOR_LIGHT_GRAY = 0xAD6B;
    const ushort COLOR_WHITE = 0xFFFF;

    const byte COLOR_PALETTE_SIZE = 64;

    public enum Modes : byte { HorizontalBlank, VerticalBlank, OamSearch, PixelTransfer }
    public enum CoincidenceFlagModes : byte { Different, Equals }

    int clocksToWait;
    int currentBuffer;

    readonly byte[] currentLineColorIndices = new byte[Constants.RES_X];

    public delegate void FinishedVBlankDelegate();

    public PPU(GameBoy gameBoy, InterruptHandler interruptHandler, VRAM vram, OAM oam, DMA dma, ScreenUpdateDelegate screenUpdateCallback, FinishedVBlankDelegate finishedVBlankCallback)
    {
        this.gameBoy = gameBoy;
        this.interruptHandler = interruptHandler;
        this.vram = vram;
        this.oam = oam;
        this.dma = dma;
        this.screenUpdateCallback = screenUpdateCallback;
        this.finishedVBlankCallback = finishedVBlankCallback;

        // Initialize double buffer
        lcdPixelBuffers = new ushort[2][];
        lcdPixelBuffers[0] = new ushort[Constants.RES_X * Constants.RES_Y];
        lcdPixelBuffers[1] = new ushort[Constants.RES_X * Constants.RES_Y];
        currentBuffer = 0;

        if (gameBoy.IsColorMode)
        {
            colorBgPalette = new byte[COLOR_PALETTE_SIZE];
            colorObjPalette = new byte[COLOR_PALETTE_SIZE];

            for (int i = 0; i < COLOR_PALETTE_SIZE; i++)
            {
                colorBgPalette[i] = 0xFF;
                colorObjPalette[i] = 0xFF;
            }
        }

        spriteIndicesInCurrentLine = new List<byte>(MAX_SPRITES_PER_LINE);

        DoOAMSearch();
    }

    public byte this[int index]
    {
        get
        {
            if (index >= VRAM.START_ADDRESS && index < VRAM.END_ADDRESS)
            {
                if (CanCPUAccessVRAM())
                {
                    return vram[index];
                }
                else
                {
                    Utils.Log(LogType.Warning, $"Tried to read from VRAM while in {LCDStatusMode} mode.");
                    return 0xFF;
                }
            }
            else if (index >= OAM.START_ADDRESS && index < OAM.END_ADDRESS)
            {
                if (CanCPUAccessOAM())
                {
                    return oam[index - OAM.START_ADDRESS];
                }
                else
                {
                    Utils.Log(LogType.Warning, $"Tried to read from OAM while in {LCDStatusMode} mode.");
                    return 0xFF;
                }
            }
            else
            {
                throw new IndexOutOfRangeException("Tried to read out of range PPU memory.");
            }
        }

        set
        {
            if (index >= VRAM.START_ADDRESS && index < VRAM.END_ADDRESS)
            {
                if (CanCPUAccessVRAM())
                    vram[index] = value;
                else
                    Utils.Log(LogType.Warning, $"Tried to write to VRAM while in {LCDStatusMode} mode.");
            }
            else if (index >= OAM.START_ADDRESS && index < OAM.END_ADDRESS)
            {
                if (CanCPUAccessOAM())
                    oam[index - OAM.START_ADDRESS] = value;
                else
                    Utils.Log(LogType.Warning, $"Tried to write to OAM while in {LCDStatusMode} mode.");
            }
            else
            {
                throw new IndexOutOfRangeException("Tried to write out of range PPU memory.");
            }
        }
    }

    public void Dispose()
    {
        for (int p = 0; p < Constants.RES_X * Constants.RES_Y; p++)
        {
            lcdPixelBuffers[currentBuffer][p] = 0;
        }

        screenUpdateCallback?.Invoke(lcdPixelBuffers[currentBuffer]);
    }

    public void Update()
    {
        if (!LCDDisplayEnable)
        {
            LCDStatusMode = Modes.HorizontalBlank;
            LY = 0;

            // HACK: Extra clocks than usual for when reenabling the LCD.
            // Value found by trial and error.
            // This is the one that makes the test oam_bug/rom_singles/1-lcd_sync.gb to pass.
            // Edit: Disabled again, causes error in Alleyway.
            // clocksToWait = 452;
            return;
        }

        clocksToWait -= 4;
        if (clocksToWait > 0) return;

        switch (LCDStatusMode)
        {
            case Modes.OamSearch:

                DoPixelTransfer();

                break;

            case Modes.PixelTransfer:

                DoHorizontalBlank();

                break;

            case Modes.HorizontalBlank:

                LY++;
                CheckLYC();

                if (LY >= Constants.RES_Y)
                {
                    DoVerticalBlank();
                }
                else
                {
                    DoOAMSearch();
                }

                break;

            case Modes.VerticalBlank:

                LY++;

                if (LY >= Constants.RES_Y + VERTICAL_BLANK_LINES)
                {
                    screenUpdateCallback?.Invoke(lcdPixelBuffers[currentBuffer]);
                    currentBuffer ^= 1;

                    LY = 0;
                    CheckLYC();
                    finishedVBlankCallback?.Invoke();
                    DoOAMSearch();
                }
                else
                {
                    CheckLYC();
                    clocksToWait = VERTICAL_BLANK_CLOCKS;
                }

                break;
        }
    }

    /// <summary>
    /// This is for the OAM Bug in DMG.
    /// </summary>
    public void CorruptOAM(ushort modifiedAddress)
    {
        if (!LCDDisplayEnable) return;
        if (LCDStatusMode != Modes.OamSearch) return;
        if (modifiedAddress < OAM.START_ADDRESS || modifiedAddress >= VRAM.UNUSABLE_END_ADDRESS - 1) return;

        for (int m = 0; m < OAM.SIZE; m++)
        {
            oam[m] = (byte)m;
        }
    }

    void DoOAMSearch()
    {
        LCDStatusMode = Modes.OamSearch;
        clocksToWait = OAM_SEARCH_CLOCKS;

        if (LCDStatusOAMSearchInterrupt)
        {
            interruptHandler.RequestLCDCSTAT = true;
        }

        spriteIndicesInCurrentLine.Clear();

        byte spriteHeight = OBJSize ? SPRITE_MAX_HEIGHT : SPRITE_HEIGHT;

        for (byte s = 0; s < MAX_SPRITES; s++)
        {
            int spriteEntryAddress = s * OAM_ENTRY_SIZE;

            int spriteY = GetOAM(spriteEntryAddress + 0) - SPRITE_MAX_HEIGHT;
            if (LY >= spriteY + spriteHeight) continue;
            if (LY < spriteY) continue;

            //byte spriteX = GetOAM(spriteEntryAddress + 1);
            //if (spriteX == 0) continue;

            // TODO: Check more conditions?

            spriteIndicesInCurrentLine.Add(s);
            if (spriteIndicesInCurrentLine.Count >= MAX_SPRITES_PER_LINE) break;
        }

        if (!gameBoy.IsColorMode)
        {
            // Sort by X coordinate on DMG. Overlapping sprites with smaller X will render on top.

            spriteIndicesInCurrentLine.Sort((byte s1, byte s2) =>
            {
                int sprite1X = GetOAM((s1 * OAM_ENTRY_SIZE) + 1);
                int sprite2X = GetOAM((s2 * OAM_ENTRY_SIZE) + 1);
                return sprite1X.CompareTo(sprite2X);
            });
        }
    }

    void DoPixelTransfer()
    {
        LCDStatusMode = Modes.PixelTransfer;
        clocksToWait = PIXEL_TRANSFER_CLOCKS;

        if (BGDisplayEnable)
        {
            ushort tileMapAddress = BGTileMapDisplaySelect ? VRAM.START_TILE_MAP_2_ADDRESS : VRAM.START_TILE_MAP_1_ADDRESS;
            ushort tileDataAddress = BGAndWindowTileDataSelect ? VRAM.START_TILE_DATA_1_ADDRESS : VRAM.START_TILE_DATA_2_ADDRESS;

            int sY = (LY + ScrollY) & 0xFF;

            for (int x = 0; x < Constants.RES_X; x++)
            {
                int sX = (x + ScrollX) & 0xFF;

                ushort currentTileMapAddress = tileMapAddress;
                currentTileMapAddress += (ushort)((sY >> 3) * BG_TILES_X + (sX >> 3));

                byte tile = vram.GetRawBytes(currentTileMapAddress);
                if (!BGAndWindowTileDataSelect) tile = (byte)((tile + 0x80) & 0xFF);

                ushort currentTileDataAddress = tileDataAddress;

                byte bitX = (byte)(7 - (sX & 0x7));
                byte bitY = (byte)(sY & 0x7);

                byte palette = 0;

                if (gameBoy.IsColorMode)
                {
                    byte attributes = vram.GetRawBytes(currentTileMapAddress + VRAM.DMG_SIZE);
                    palette = (byte)(attributes & 0b0000_0111);
                    bool bank = (attributes & 0b0000_1000) != 0;
                    bool invX = (attributes & 0b0010_0000) != 0;
                    bool invY = (attributes & 0b0100_0000) != 0;
                    bool priority = (attributes & 0b1000_0000) != 0;

                    if (bank) currentTileDataAddress += VRAM.DMG_SIZE;
                    if (invX) bitX = (byte)(7 - bitX);
                    if (invY) bitY = (byte)(7 - bitY);
                }

                currentTileDataAddress += (ushort)(tile * TILE_BYTES_SIZE + (bitY << 1));

                int currentLCDPixel = LY * Constants.RES_X + (x);

                if (currentTileMapAddress == 0x9a42)
                {
                    int lmao = 0;
                }

                currentLineColorIndices[x] = GetColorIndex(currentTileDataAddress, bitX);
                lcdPixelBuffers[currentBuffer][currentLCDPixel] = GetBGColor(currentLineColorIndices[x], palette);
            }
        }
        else
        {
            for (int x = 0; x < Constants.RES_X; x++)
            {
                int currentLCDPixel = LY * Constants.RES_X + x;
                lcdPixelBuffers[currentBuffer][currentLCDPixel] = COLOR_WHITE;
            }
        }

        if (WindowDisplayEnable)
        {
            ushort tileMapAddress = WindowTileMapDisplaySelect ? VRAM.START_TILE_MAP_2_ADDRESS : VRAM.START_TILE_MAP_1_ADDRESS;
            ushort tileDataAddress = BGAndWindowTileDataSelect ? VRAM.START_TILE_DATA_1_ADDRESS : VRAM.START_TILE_DATA_2_ADDRESS;

            int sY = (LY - WindowY) & 0xFF;

            for (int x = 0; x < Constants.RES_X; x++)
            {
                if (LY >= WindowY + Constants.RES_Y) break;
                if (LY < WindowY) break;

                if (x > WindowX - 7 + Constants.RES_X) continue;
                if (x < WindowX - 7) continue;

                int sX = (x - WindowX + 7) & 0xFF;

                ushort currentTileMapAddress = tileMapAddress;
                currentTileMapAddress += (ushort)((sY >> 3) * BG_TILES_X + (sX >> 3));

                byte tile = vram.GetRawBytes(currentTileMapAddress);
                if (!BGAndWindowTileDataSelect) tile = (byte)((tile + 0x80) & 0xFF);

                ushort currentTileDataAddress = tileDataAddress;

                byte bitX = (byte)(7 - (sX & 0x7));
                byte bitY = (byte)(sY & 0x7);

                byte palette = 0;

                if (gameBoy.IsColorMode)
                {
                    byte attributes = vram.GetRawBytes(currentTileMapAddress + VRAM.DMG_SIZE);
                    palette = (byte)(attributes & 0b0000_0111);
                    bool bank = (attributes & 0b0000_1000) != 0;
                    bool invX = (attributes & 0b0010_0000) != 0;
                    bool invY = (attributes & 0b0100_0000) != 0;
                    bool priority = (attributes & 0b1000_0000) != 0;

                    if (bank) currentTileDataAddress += VRAM.DMG_SIZE;
                    if (invX) bitX = (byte)(7 - bitX);
                    if (invY) bitY = (byte)(7 - bitY);
                }

                currentTileDataAddress += (ushort)(tile * TILE_BYTES_SIZE + (bitY << 1));

                int currentLCDPixel = LY * Constants.RES_X + x;

                currentLineColorIndices[x] = GetColorIndex(currentTileDataAddress, bitX);
                lcdPixelBuffers[currentBuffer][currentLCDPixel] = GetBGColor(currentLineColorIndices[x], palette);
            }
        }

        if (OBJDisplayEnable)
        {
            byte spriteHeight = OBJSize ? SPRITE_MAX_HEIGHT : SPRITE_HEIGHT;

            for (int s = spriteIndicesInCurrentLine.Count - 1; s >= 0; s--)
            {
                byte spriteIndex = spriteIndicesInCurrentLine[s];
                int spriteEntryAddress = spriteIndex * OAM_ENTRY_SIZE;

                int spriteY = GetOAM(spriteEntryAddress + 0) - SPRITE_MAX_HEIGHT;
                int spriteX = GetOAM(spriteEntryAddress + 1) - SPRITE_WIDTH;

                byte spritePalette;

                if (gameBoy.IsColorMode) spritePalette = (byte)(GetOAM(spriteEntryAddress + 3) & 0b0000_0111);
                else spritePalette = (byte)(Helpers.GetBit(GetOAM(spriteEntryAddress + 3), 4) ? 1 : 0);
                bool spriteInvX = Helpers.GetBit(GetOAM(spriteEntryAddress + 3), 5);
                bool spriteInvY = Helpers.GetBit(GetOAM(spriteEntryAddress + 3), 6);
                bool spritePriority = Helpers.GetBit(GetOAM(spriteEntryAddress + 3), 7);

                byte spriteTile = GetOAM(spriteEntryAddress + 2);
                int spriteRow = (spriteInvY ? ((spriteHeight - 1) - (LY - spriteY)) : (LY - spriteY)) << 1;

                if (spriteRow < 0)
                {
                    Utils.Log(LogType.Warning, $"spriteRow < 0! spriteEntryAddress = 0x{spriteEntryAddress:x4}");
                    continue;
                }

                ushort tileDataAddress = (ushort)(spriteTile * TILE_BYTES_SIZE + spriteRow + VRAM.START_ADDRESS);

                // Prevent sprites being able to draw data from the BG exclusive area.
                // Some games like Super Bikkuriman try to do this, and it should not be rendered.
                if (tileDataAddress >= 0x1000 + VRAM.START_ADDRESS) continue;

                int minX = Math.Max(spriteX, 0);
                int maxX = Math.Min(spriteX + 8, Constants.RES_X);

                for (int x = minX; x < maxX; x++)
                {
                    int currentLCDPixel = LY * Constants.RES_X + x;
                    byte bit = (byte)(spriteInvX ? x - spriteX : (SPRITE_WIDTH - 1) - (x - spriteX));
                    byte colorIndex = GetColorIndex(tileDataAddress, bit);
                    if (colorIndex != 0)
                    {
                        if (spritePriority)
                        {
                            if (currentLineColorIndices[x] == 0) lcdPixelBuffers[currentBuffer][currentLCDPixel] = GetSpriteColor(colorIndex, spritePalette);
                        }
                        else
                        {
                            lcdPixelBuffers[currentBuffer][currentLCDPixel] = GetSpriteColor(colorIndex, spritePalette);
                        }
                    }
                }
            }
        }
    }

    void DoHorizontalBlank()
    {
        LCDStatusMode = Modes.HorizontalBlank;
        clocksToWait = HORIZONTAL_BLANK_CLOCKS;

        if (LCDStatusHorizontalBlankInterrupt)
        {
            interruptHandler.RequestLCDCSTAT = true;
        }
    }

    void DoVerticalBlank()
    {
        LCDStatusMode = Modes.VerticalBlank;
        interruptHandler.RequestVerticalBlanking = true;
        clocksToWait = VERTICAL_BLANK_CLOCKS;

        if (LCDStatusVerticalBlankInterrupt)
        {
            interruptHandler.RequestLCDCSTAT = true;
        }
    }

    void CheckLYC()
    {
        if (LY == LYC)
        {
            LCDStatusCoincidenceFlag = CoincidenceFlagModes.Equals;

            if (LCDStatusCoincidenceInterrupt)
            {
                interruptHandler.RequestLCDCSTAT = true;
            }
        }
        else
        {
            LCDStatusCoincidenceFlag = CoincidenceFlagModes.Different;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte GetColorIndex(ushort pixelAddress, byte bit)
    {
        int v1 = (vram.GetRawBytes(pixelAddress + 0) & (1 << bit)) != 0 ? 1 : 0;
        int v2 = (vram.GetRawBytes(pixelAddress + 1) & (1 << bit)) != 0 ? 1 : 0;
        return (byte)((v2 << 1) | v1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ushort GetBGColor(byte colorIndex, byte palette)
    {
        if (gameBoy.IsColorMode)
        {
            int paletteIndex = (palette << 3) | (colorIndex << 1);
            int color = (colorBgPalette[paletteIndex + 1] << 8) | colorBgPalette[paletteIndex + 0];
            color <<= 1; // Bgra texture format
            //color = (ushort)((color << 11) | ((color & 0x3e0) << 1) | ((color & 0x7c00) >> 9) | 1); // Rgba texture format

            return (ushort)color;
        }
        else
        {// 01 23 45 67 - 89 AB CD EF
            switch (GetBGPaletteColor(colorIndex))
            {
                case 0: return COLOR_WHITE;
                case 1: return COLOR_LIGHT_GRAY;
                case 2: return COLOR_DARK_GRAY;
                case 3: return COLOR_BLACK;
                default: throw new ArgumentException("Not valid BG palette color index: " + colorIndex);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ushort GetSpriteColor(byte colorIndex, byte palette)
    {
        if (gameBoy.IsColorMode)
        {
            int paletteIndex = (palette << 3) | (colorIndex << 1);
            int color = (colorObjPalette[paletteIndex + 1] << 8) | colorObjPalette[paletteIndex + 0];
            color <<= 1; // Bgra texture format

            return (ushort)color;
        }
        else
        {
            byte color = palette > 0 ? GetObjPalette1Color(colorIndex) : GetObjPalette0Color(colorIndex);

            switch (color)
            {
                case 0: return COLOR_WHITE;
                case 1: return COLOR_LIGHT_GRAY;
                case 2: return COLOR_DARK_GRAY;
                case 3: return COLOR_BLACK;
                default: throw new ArgumentException($"Not valid Obj palette {palette} color index: {colorIndex}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte GetBGPaletteColor(byte colorIndex)
    {
        return (byte)((BackgroundPalette >> (colorIndex << 1)) & 0x3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte GetObjPalette0Color(byte colorIndex)
    {
        return (byte)((ObjectPalette0 >> (colorIndex << 1)) & 0x3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte GetObjPalette1Color(byte colorIndex)
    {
        return (byte)((ObjectPalette1 >> (colorIndex << 1)) & 0x3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    byte GetOAM(int index)
    {
        return dma.IsBusy ? (byte)0xFF : oam[index];
    }

    bool CanCPUAccessVRAM()
    {
        if (!LCDDisplayEnable) return true;
        if (LCDStatusMode != Modes.PixelTransfer) return true;
        return false;
    }

    bool CanCPUAccessOAM()
    {
        if (!LCDDisplayEnable) return true;
        if (LCDStatusMode == Modes.HorizontalBlank) return true;
        if (LCDStatusMode == Modes.VerticalBlank) return true;
        return false;
    }

    void ClearScreen()
    {
        for (int p = 0; p < Constants.RES_X * Constants.RES_Y; p++) lcdPixelBuffers[currentBuffer][p] = COLOR_WHITE;
        screenUpdateCallback?.Invoke(lcdPixelBuffers[currentBuffer]);
        currentBuffer ^= 1;
    }

    public void SaveOrLoadState(Stream stream, BinaryWriter bw, BinaryReader br, bool save)
    {
        LCDDisplayEnable = SaveState.SaveLoadValue(bw, br, save, LCDDisplayEnable);
        WindowTileMapDisplaySelect = SaveState.SaveLoadValue(bw, br, save, WindowTileMapDisplaySelect);
        WindowDisplayEnable = SaveState.SaveLoadValue(bw, br, save, WindowDisplayEnable);
        BGAndWindowTileDataSelect = SaveState.SaveLoadValue(bw, br, save, BGAndWindowTileDataSelect);
        BGTileMapDisplaySelect = SaveState.SaveLoadValue(bw, br, save, BGTileMapDisplaySelect);
        OBJSize = SaveState.SaveLoadValue(bw, br, save, OBJSize);
        OBJDisplayEnable = SaveState.SaveLoadValue(bw, br, save, OBJDisplayEnable);
        BGDisplayEnable = SaveState.SaveLoadValue(bw, br, save, BGDisplayEnable);

        LCDStatusMode = (Modes)SaveState.SaveLoadValue(bw, br, save, (byte)LCDStatusMode);
        LCDStatusCoincidenceFlag = (CoincidenceFlagModes)SaveState.SaveLoadValue(bw, br, save, (byte)LCDStatusCoincidenceFlag);
        LCDStatusHorizontalBlankInterrupt = SaveState.SaveLoadValue(bw, br, save, LCDStatusHorizontalBlankInterrupt);
        LCDStatusVerticalBlankInterrupt = SaveState.SaveLoadValue(bw, br, save, LCDStatusVerticalBlankInterrupt);
        LCDStatusOAMSearchInterrupt = SaveState.SaveLoadValue(bw, br, save, LCDStatusOAMSearchInterrupt);
        LCDStatusCoincidenceInterrupt = SaveState.SaveLoadValue(bw, br, save, LCDStatusCoincidenceInterrupt);

        ScrollY = SaveState.SaveLoadValue(bw, br, save, ScrollY);
        ScrollX = SaveState.SaveLoadValue(bw, br, save, ScrollX);
        LY = SaveState.SaveLoadValue(bw, br, save, LY);
        LYC = SaveState.SaveLoadValue(bw, br, save, LYC);
        WindowY = SaveState.SaveLoadValue(bw, br, save, WindowY);
        WindowX = SaveState.SaveLoadValue(bw, br, save, WindowX);

        BackgroundPalette = SaveState.SaveLoadValue(bw, br, save, BackgroundPalette);
        ObjectPalette0 = SaveState.SaveLoadValue(bw, br, save, ObjectPalette0);
        ObjectPalette1 = SaveState.SaveLoadValue(bw, br, save, ObjectPalette1);

        if (gameBoy.IsColorMode)
        {
            SaveState.SaveLoadArray(stream, save, colorBgPalette, colorBgPalette.Length);
            ColorBgPaletteAddress = SaveState.SaveLoadValue(bw, br, save, ColorBgPaletteAddress);
            ColorBgPaletteAutoIncrement = SaveState.SaveLoadValue(bw, br, save, ColorBgPaletteAutoIncrement);

            SaveState.SaveLoadArray(stream, save, colorObjPalette, colorObjPalette.Length);
            ColorObjPaletteAddress = SaveState.SaveLoadValue(bw, br, save, ColorObjPaletteAddress);
            ColorObjPaletteAutoIncrement = SaveState.SaveLoadValue(bw, br, save, ColorObjPaletteAutoIncrement);
        }
    }
}