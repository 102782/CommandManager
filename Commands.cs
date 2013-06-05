using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VJoyDemo;
using System.Threading;
using System.Text.RegularExpressions;

namespace CommandManager
{
    internal interface ICommand
    {
        void Execute();
    }

    internal class Command : ICommand
    {
        public CommandExecuter executer;
        public int id { get; set; }
        public bool flag { get; set; }

        public virtual void Execute()
        {
            throw new NotImplementedException();
        }
    }

    internal class InputCommand : Command
    {
        private int? button { get; set; }
        private short X { get; set; }
        private short Y { get; set; }
        private int frame { get; set; }

        public InputCommand(CommandExecuter executer, int id, string command)
        {
            this.executer = executer;
            this.id = id;
            var m = this.executer.parser.Match(command);

            this.X = 0;
            this.Y = 0;
            if (m.Groups["direction"].Success && !string.IsNullOrEmpty(m.Groups["direction"].Value))
            {
                var direction = int.Parse(m.Groups["direction"].Value);
                this.X = ConvertDirectionValue(((direction + 2) % 3) - 1);
                this.Y = ConvertDirectionValue((((direction + 2) / 3) - 2) * -1);
            }

            this.button = null;
            if (m.Groups["button"].Success
                && !string.IsNullOrEmpty(m.Groups["button"].Value)
                && this.executer.assign.ContainsKey(m.Groups["button"].Value))
            {
                this.button = this.executer.assign[m.Groups["button"].Value];
            }

            this.frame = int.Parse(m.Groups["frame"].Value);

            this.flag = bool.Parse(m.Groups["flag"].Value);
        }

        public override string ToString()
        {
            return string.Format("({0},{1}), {2}, {3}F, {4}", 
                this.X, this.Y, this.button, this.frame, this.flag);
        }

        override public void Execute()
        {
            this.executer.flag = this.flag;
            this.executer.vjoy.Reset();
            if (this.button != null)
            {
                this.executer.vjoy.SetButton(0, this.button.Value, true);
            }
            this.executer.vjoy.SetXAxis(0, this.X);
            this.executer.vjoy.SetYAxis(0, this.Y);
            this.executer.waitFrame = this.frame;
            this.executer.isKeyUpdated = true;
        }

        private short ConvertDirectionValue(int direction)
        {
            short value = 0;
            if (direction == 1)
            {
                value = 32767;
            }
            else if (direction == -1)
            {
                value = -32767;
            }
            return value;
        }
    }

    public class CommandExecuter
    {
        public VJoy vjoy { get; private set; }
        private Queue<ICommand> commands;
        public bool isKeyUpdated { get; set; }
        public bool flag { get; set; }
        private Task keyInputer { get; set; }
        public bool isKeyInputing { get; set; }
        public int waitFrame { get; set; }
        public Dictionary<string, int> assign { get; private set; }
        public Regex parser { get; private set; }

        public long frame { get; private set; }
        private long oldFrame { get; set; }
        public bool isFrameChanged { get; private set; }
        public Func<long> frameUpdating { get; set; }
        public Action updateStarted { get; set; }
        public Action frameChanged { get; set; }
        public Action updateStopped { get; set; }
        public bool isFrameUpdating { get; private set; }
        private Task frameUpdater;

        public CommandExecuter() { }

        public void Initialize(Dictionary<string, int> assign, 
            Func<long> frameUpdating, Action updateStarted, Action frameChanged, Action updateStopped)
        {
            this.isKeyInputing = false;
            this.isKeyUpdated = false;
            this.flag = false;
            this.vjoy = new VJoy();
            this.vjoy.Initialize();
            this.vjoy.Reset();
            this.vjoy.Update(0);
            this.vjoy.Update(1);
            this.commands = new Queue<ICommand>();
            this.waitFrame = 0;
            this.assign = new Dictionary<string, int>();
            this.assign = assign;
            this.parser = new Regex(string.Format(@"(?<direction>[1-9]?)(?<button>[N{0}]?)\((?<frame>\d+)F,\s?(?<flag>true|false|True|False)\)", 
                string.Join("", (this.assign.Keys.ToArray()))));

            this.frameUpdating = frameUpdating;
            this.updateStarted = updateStarted;
            this.frameChanged = frameChanged;
            this.updateStopped = updateStopped;
        }

        public void StartUpdate()
        {
            this.isFrameUpdating = true;
            this.frameUpdater = Task.Factory.StartNew(()=>
            {
                this.updateStarted();
            })
            .ContinueWith((_) =>
            {
                while (this.isFrameUpdating)
                {
                    this.oldFrame = this.frame;
                    this.frame = this.frameUpdating();
                    this.isFrameChanged = (this.frame - this.oldFrame) != 0;

                    if (this.isFrameChanged)
                    {
                        this.Update();
                        this.frameChanged();
                    }
                    Thread.Sleep(0);
                }
            })
            .ContinueWith((_) =>
            {
                this.updateStopped();
            });
        }

        public void StopUpdate()
        {
            this.isFrameUpdating = false;
            if (this.frameUpdater != null && !this.frameUpdater.IsCompleted)
            {
                this.frameUpdater.Wait();
            }
        }

        public void Shutdown()
        {
            if (this.keyInputer != null && !this.keyInputer.IsCompleted)
            {
                this.keyInputer.Wait();
            }
            StopUpdate();
            this.vjoy.Shutdown();
        }

        public void CreateCommands(string commandString)
        {
            commandString.Split('>').ForEach((s) =>
            {
                if (this.parser.IsMatch(s))
                {
                    this.commands.Enqueue(new InputCommand(this, this.commands.Count, s));
                }
            });
        }

        public void ExecuteCommandsEx()
        {
            this.keyInputer = Task.Factory.StartNew(() =>
            {
                this.isKeyInputing = true;
            })
            .ContinueWith((_) =>
            {
                this.commands.ForEach((c) =>
                {
                    c.Execute();
                    if (this.isKeyUpdated)
                    {
                        this.vjoy.Update(0);
                        Thread.Sleep(17 * this.waitFrame);
                    }
                    this.isKeyUpdated = false;
                });
            })
            .ContinueWith((_) =>
            {
                this.vjoy.Reset();
                this.vjoy.Update(0);
                Thread.Sleep(17);
                this.commands.Clear();
                this.waitFrame = 0;
                this.isKeyInputing = false;
            });
        }

        public void Update()
        {
            if (this.waitFrame == 0)
            {
                if (this.commands.Any())
                {
                    this.commands.Dequeue().Execute();
                }
                else
                {
                    this.vjoy.Reset();
                    this.isKeyUpdated = true;
                }
            }
            else
            {
                this.waitFrame -= 1;
            }

            if (this.isKeyUpdated)
            {
                this.vjoy.Update(0);
                this.isKeyUpdated = false;
            }
        }
    }
}
