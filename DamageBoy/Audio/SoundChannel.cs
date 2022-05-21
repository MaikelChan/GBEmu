﻿using DamageBoy.Core;
using OpenTK.Audio.OpenAL;
using System;

namespace DamageBoy.Audio
{
    class SoundChannel : IDisposable
    {
        readonly Func<byte[], bool> fillAudioBufferCallback;

        readonly int[] alBuffers;
        readonly byte[] soundData;
        //int soundDataPosition;
        int alSource;

        //enum AudioStates { Buffering, Playing }
        //AudioStates audioState;

        public Sound.BufferStates BufferState { get; private set; }

        public const int BUFFERS_PER_CHANNEL = 2;
        public const int BUFFER_SIZE = 2048;

        public SoundChannel(Func<byte[], bool> fillAudioBufferCallback)
        {
            this.fillAudioBufferCallback = fillAudioBufferCallback;

            alBuffers = new int[BUFFERS_PER_CHANNEL];
            for (int b = 0; b < BUFFERS_PER_CHANNEL; b++) alBuffers[b] = AL.GenBuffer();

            soundData = new byte[BUFFER_SIZE];

            //soundDataPosition = 0;
            //audioState = AudioStates.Buffering;
            //BufferState = Sound.BufferStates.Ok;
        }

        public void Dispose()
        {
            for (int b = 0; b < BUFFERS_PER_CHANNEL; b++)
            {
                AL.DeleteBuffer(alBuffers[b]);
                alBuffers[b] = 0;
            }

            DeleteSource();
        }

        public void ProcessChannel()
        {
            InitializeSource();

            AL.GetSource(alSource, ALGetSourcei.SourceState, out int sourceState);

            switch ((ALSourceState)sourceState)
            {
                case ALSourceState.Initial:

                    fillAudioBufferCallback.Invoke(soundData);

                    for (int b = 0; b < BUFFERS_PER_CHANNEL; b++)
                    {
                        AL.BufferData(alBuffers[b], ALFormat.Stereo8, ref soundData[0], BUFFER_SIZE, Constants.SAMPLE_RATE);
                        AL.SourceQueueBuffer(alSource, alBuffers[b]);
                    }

                    AL.SourcePlay(alSource);

                    break;

                case ALSourceState.Playing:

                    AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out int buffersProcessed);

                    if (buffersProcessed > 0)
                    {
                        if (buffersProcessed != 1) Utils.Log(LogType.Warning, "Buffers processed: " + buffersProcessed);

                        bool isFilled = fillAudioBufferCallback.Invoke(soundData);
                        if (!isFilled) return;

                        int unqueuedBuffer = AL.SourceUnqueueBuffer(alSource);

                        AL.BufferData(unqueuedBuffer, ALFormat.Stereo8, ref soundData[0], BUFFER_SIZE, Constants.SAMPLE_RATE);
                        AL.SourceQueueBuffer(alSource, unqueuedBuffer);

                        //soundDataPosition = 0;
                    }

                    //AL.BufferData(unqueuedBuffer, ALFormat.Stereo8, ref soundData[0][0], BUFFER_SIZE, Constants.SAMPLE_RATE);
                    //AL.SourceQueueBuffer(alSource, unqueuedBuffer);

                    break;

                case ALSourceState.Paused:

                    Utils.Log(LogType.Error, "Audio source is paused. This should not be happening.");

                    break;

                case ALSourceState.Stopped:

                    Utils.Log(LogType.Warning, "Audio buffer underrun. Reinitializing it...");

                    // Need to fully delete the source before starting playback again.
                    // I haven't seen a way to unqueue all buffers, which is required
                    // to not have old audio playing when starting playback again).
                    // SourceUnqueueBuffer() only unqueues processed ones, not all of them.

                    DeleteSource();

                    break;
            }

            //if (audioState == AudioStates.Buffering)
            //{
            //    if (!data.HasValue) return;

            //    soundData[soundDataPosition + 0] = (byte)(data.Value >> 8);
            //    soundData[soundDataPosition + 1] = (byte)(data.Value & 0xFF);
            //    soundDataPosition += 2;

            //    if (soundDataPosition < BUFFERS_PER_CHANNEL * BUFFER_SIZE) return;

            //    soundDataPosition = 0;
            //    InitializeSource();

            //    for (int b = 0; b < BUFFERS_PER_CHANNEL; b++)
            //    {
            //        AL.BufferData(alBuffers[b], ALFormat.Stereo8, ref soundData[b * BUFFER_SIZE], BUFFER_SIZE, Constants.SAMPLE_RATE);
            //        AL.SourceQueueBuffer(alSource, alBuffers[b]);
            //    }

            //    audioState = AudioStates.Playing;
            //    AL.SourcePlay(alSource);
            //}
            //else
            //{
            //    AL.GetSource(alSource, ALGetSourcei.SourceState, out int sourceState);
            //    if (sourceState != (int)ALSourceState.Playing)
            //    {
            //        // Need to fully delete the source before starting playback again.
            //        // I haven't seen a way to unqueue all buffers, which is required
            //        // to not have old audio playing when starting playback again).
            //        // SourceUnqueueBuffer() only unqueues processed ones, not all of them.

            //        DeleteSource();
            //        return;
            //    }

            //    if (soundDataPosition < BUFFER_SIZE && data.HasValue)
            //    {
            //        soundData[soundDataPosition + 0] = (byte)(data.Value >> 8);
            //        soundData[soundDataPosition + 1] = (byte)(data.Value & 0xFF);
            //        soundDataPosition += 2;
            //    }

            //    AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out int buffersProcessed);

            //    if (buffersProcessed > 0)
            //    {
            //        if (soundDataPosition < BUFFER_SIZE)
            //        {
            //            BufferState = Sound.BufferStates.Underrun;
            //            return;
            //        }
            //        else
            //        {
            //            BufferState = Sound.BufferStates.Ok;
            //        }

            //        //Resample(soundData, soundDataPosition, resampledSoundData, BUFFER_SIZE);
            //        //Utils.Log($"Buffers processed: {buffersProcessed}, Buffer Size: {soundDataPosition}, Expected Buffer Size: {BUFFER_SIZE}");

            //        int unqueuedBuffer = AL.SourceUnqueueBuffer(alSource);

            //        AL.BufferData(unqueuedBuffer, ALFormat.Stereo8, ref soundData[0], BUFFER_SIZE, Constants.SAMPLE_RATE);
            //        AL.SourceQueueBuffer(alSource, unqueuedBuffer);

            //        soundDataPosition = 0;
            //    }
            //    else
            //    {
            //        if (soundDataPosition >= BUFFER_SIZE)
            //        {
            //            BufferState = Sound.BufferStates.Overrun;
            //        }
            //        else
            //        {
            //            BufferState = Sound.BufferStates.Ok;
            //        }
            //    }
            //}
        }

        void InitializeSource()
        {
            if (alSource > 0) return;

            alSource = AL.GenSource();
            AL.Source(alSource, ALSourcef.Gain, 1f);
            AL.Source(alSource, ALSourceb.Looping, false);
        }

        public void DeleteSource()
        {
            if (alSource <= 0) return;

            AL.SourceStop(alSource);
            AL.DeleteSource(alSource);
            alSource = 0;

            //audioState = AudioStates.Buffering;
            //BufferState = Sound.BufferStates.Ok;
            //soundDataPosition = 0;
        }

        //static void Resample(byte[] source, int sourceLength, byte[] destination, int destinationLength)
        //{
        //    float d = (float)sourceLength / destinationLength;

        //    for (int b = 0; b < destinationLength; b++)
        //    {
        //        //destination[b] = source[(int)(b * d)];

        //        if (b < sourceLength)
        //            destination[b] = source[b];
        //        else
        //            destination[b] = 127;
        //    }
        //}
    }
}