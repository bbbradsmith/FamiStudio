﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;

namespace FamiStudio
{
    public class OpenALStream
    {
        private static AudioContext context;

        public delegate short[] GetBufferDataCallback();
        private GetBufferDataCallback bufferFill;
        private int freq;
        private bool quit;
        private Task playingTask;

        int source;
        int[] buffers;

        public OpenALStream(int rate, int channels, int bufferSize, int numBuffers, GetBufferDataCallback bufferFillCallback)
        {
            if (context == null)
                context = new AudioContext();

            freq = rate;
            source = AL.GenSource();
            buffers = AL.GenBuffers(numBuffers);
            bufferFill = bufferFillCallback;
            quit = false;
        }

        public void Dispose()
        {
            AL.DeleteBuffers(buffers);
            AL.DeleteSource(source);
        }

        public bool IsStarted => playingTask != null;

        public void Start()
        {
            quit = false;
            playingTask = Task.Factory.StartNew(PlayAsync, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            if (playingTask != null)
            {
                quit = true;
                playingTask.Wait();
                playingTask = null;
            }

            AL.SourceStop(source);
            AL.Source(source, ALSourcei.Buffer, 0);
        }

        private void DebugStream()
        {
#if DEBUG
            AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int numProcessed);
            AL.GetSource(source, ALGetSourcei.BuffersQueued, out int numQueued);
            AL.GetSource(source, ALGetSourcei.SourceState, out int state);
            Debug.WriteLine($"{DateTime.Now.Millisecond} = {state} {numQueued} {numProcessed}");
#endif
        }

        private unsafe void PlayAsync()
        {
            int initBufferIdx = buffers.Length - 1;

            while (!quit)
            {
                int numProcessed = 0;

                if (initBufferIdx < 0)
                {
                    do
                    {
                        AL.GetSource(source, ALGetSourcei.BuffersProcessed, out numProcessed);
                        if (numProcessed != 0) break;
                        if (quit) return;
                        Thread.Sleep(1);
                    }
                    while (true);
                }
                else
                {
                    numProcessed = initBufferIdx + 1;
                }

                for (int i = 0; i < numProcessed && !quit; )
                {
                    var data = bufferFill();
                    if (data != null)
                    {
                        int bufferId = initBufferIdx >= 0 ? buffers[initBufferIdx--] : AL.SourceUnqueueBuffer(source);
                        fixed (short* p = &data[0])
                            AL.BufferData(bufferId, ALFormat.Mono16, new IntPtr(p), data.Length * sizeof(short), freq);
                        AL.SourceQueueBuffer(source, bufferId);
                        i++;
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }

                //DebugStream();

                AL.GetSource(source, ALGetSourcei.SourceState, out int state);
                if ((ALSourceState)state != ALSourceState.Playing)
                {
                    Debug.WriteLine("RESTART!");
                    //DebugStream();
                    AL.SourcePlay(source);
                }
            }
        }
    }
}
