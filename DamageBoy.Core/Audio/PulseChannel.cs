﻿using DamageBoy.Core.State;
using System;

namespace DamageBoy.Core.Audio
{
    public enum PulsePatterns : byte
    {
        Percent12_5,
        Percent25,
        Percent50,
        Percent75
    }

    class PulseChannel : SoundChannel, ISweep, IVolumeEnvelope
    {
        // Sweep

        public byte SweepShift { get; set; }
        public SweepTypes SweepType { get; set; }
        public byte SweepTime { get; set; }

        // Pulse

        public PulsePatterns PulsePattern { get; set; }

        // Volume Envelope

        public byte LengthEnvelopeSteps { get; set; }
        public EnvelopeDirections EnvelopeDirection { get; set; }
        public byte InitialVolume { get; set; }

        // Frequency

        public byte FrequencyLo { get; set; }
        public byte FrequencyHi { get; set; }

        // Helper properties

        protected override int MaxLength => 64;
        protected override bool IsDACEnabled => InitialVolume != 0 || EnvelopeDirection == EnvelopeDirections.Increase;

        // Current state

        int currentEnvelopeTimer;
        int currentVolume;
        int currentSweepTimer;
        int currentFrequency;
        float currentWaveCycle;

        public PulseChannel(APU apu) : base(apu)
        {

        }

        protected override ushort InternalProcess(bool updateSample, bool updateVolume, bool updateSweep)
        {
            if (updateVolume)
            {
                if (LengthEnvelopeSteps > 0 && currentEnvelopeTimer > 0)
                {
                    currentEnvelopeTimer--;
                    if (currentEnvelopeTimer == 0)
                    {
                        currentEnvelopeTimer = LengthEnvelopeSteps;

                        if (EnvelopeDirection == EnvelopeDirections.Decrease)
                        {
                            currentVolume--;
                            if (currentVolume < 0) currentVolume = 0;
                        }
                        else
                        {
                            currentVolume++;
                            if (currentVolume > 0xF) currentVolume = 0xF;
                        }
                    }
                }
            }

            if (updateSweep)
            {
                if (SweepTime > 0 && currentSweepTimer > 0)
                {
                    currentSweepTimer--;
                    if (currentSweepTimer == 0)
                    {
                        currentSweepTimer = SweepTime;

                        int frequencyDifference = (int)(currentFrequency / MathF.Pow(2, SweepShift));

                        if (SweepType == SweepTypes.Increase)
                        {
                            currentFrequency += frequencyDifference;
                            if (currentFrequency > 0x7FF)
                            {
                                Stop();
                                return WAVE_SILENCE;
                            }
                        }
                        else
                        {
                            if (frequencyDifference >= 0 && SweepShift > 0)
                            {
                                currentFrequency -= frequencyDifference;
                            }
                        }
                    }
                }
            }

            if (updateSample)
            {
                float percentage;
                switch (PulsePattern)
                {
                    default:
                    case PulsePatterns.Percent12_5: percentage = 0.75f; break;
                    case PulsePatterns.Percent25: percentage = 0.5f; break;
                    case PulsePatterns.Percent50: percentage = 0.0f; break;
                    case PulsePatterns.Percent75: percentage = -0.5f; break;
                }

                float frequency = 131072f / (2048 - currentFrequency);

                currentWaveCycle += (frequency * MathF.PI * 2) / Constants.SAMPLE_RATE;
                currentWaveCycle %= Constants.SAMPLE_RATE;

                float wave = MathF.Sin(currentWaveCycle);
                wave = wave > percentage ? 1f : -0.999f;
                wave *= currentVolume / (float)0xF;
                return FloatWaveToUInt16(wave);
            }

            return WAVE_SILENCE;
        }

        public override void Initialize(bool reset)
        {
            base.Initialize(reset);

            if (reset)
            {
                currentVolume = InitialVolume;
                currentEnvelopeTimer = LengthEnvelopeSteps;
                currentSweepTimer = SweepTime;
                currentWaveCycle = 0;

                Enabled = true;
            }

            currentFrequency = (FrequencyHi << 8) | FrequencyLo;
        }

        public override void Reset()
        {
            base.Reset();

            SweepShift = 0;
            SweepType = SweepTypes.Increase;
            SweepTime = 0;

            PulsePattern = PulsePatterns.Percent12_5;

            LengthEnvelopeSteps = 0;
            EnvelopeDirection = EnvelopeDirections.Decrease;
            InitialVolume = 0;

            FrequencyLo = 0;
            FrequencyHi = 0;

            currentEnvelopeTimer = 0;
            currentVolume = 0;
            currentSweepTimer = 0;
            currentFrequency = 0;
            currentWaveCycle = 0f;
        }

        public override SoundChannelState GetState()
        {
            PulseChannelState pulseState = new PulseChannelState();

            pulseState.Enabled = Enabled;
            pulseState.LengthType = LengthType;
            pulseState.Output2 = Output2;
            pulseState.Output1 = Output1;
            pulseState.CurrentLength = currentLength;

            pulseState.SweepShift = SweepShift;
            pulseState.SweepType = SweepType;
            pulseState.SweepTime = SweepTime;

            pulseState.PulsePattern = PulsePattern;

            pulseState.LengthEnvelopeSteps = LengthEnvelopeSteps;
            pulseState.EnvelopeDirection = EnvelopeDirection;
            pulseState.InitialVolume = InitialVolume;

            pulseState.FrequencyLo = FrequencyLo;
            pulseState.FrequencyHi = FrequencyHi;

            pulseState.CurrentEnvelopeTimer = currentEnvelopeTimer;
            pulseState.CurrentVolume = currentVolume;
            pulseState.CurrentSweepTimer = currentSweepTimer;
            pulseState.CurrentFrequency = currentFrequency;
            pulseState.CurrentWaveCycle = currentWaveCycle;

            return pulseState;
        }

        public override void SetState(SoundChannelState state)
        {
            PulseChannelState pulseState = (PulseChannelState)state;

            Enabled = pulseState.Enabled;
            LengthType = pulseState.LengthType;
            Output2 = pulseState.Output2;
            Output1 = pulseState.Output1;
            currentLength = pulseState.CurrentLength;

            SweepShift = pulseState.SweepShift;
            SweepType = pulseState.SweepType;
            SweepTime = pulseState.SweepTime;

            PulsePattern = pulseState.PulsePattern;

            LengthEnvelopeSteps = pulseState.LengthEnvelopeSteps;
            EnvelopeDirection = pulseState.EnvelopeDirection;
            InitialVolume = pulseState.InitialVolume;

            FrequencyLo = pulseState.FrequencyLo;
            FrequencyHi = pulseState.FrequencyHi;

            currentEnvelopeTimer = pulseState.CurrentEnvelopeTimer;
            currentVolume = pulseState.CurrentVolume;
            currentSweepTimer = pulseState.CurrentSweepTimer;
            currentFrequency = pulseState.CurrentFrequency;
            currentWaveCycle = pulseState.CurrentWaveCycle;
        }
    }
}