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
        void Execute(CommandExecuter executer);
    }

    internal class Command : ICommand
    {
        public CommandExecuter executer;
        public int id { get; set; }
        public bool flag { get; set; }

        public virtual void Execute(CommandExecuter executer)
        {
            throw new NotImplementedException();
        }
    }

    internal class InputCommand : Command
    {
        private int direction { get; set; }
        private int? button { get; set; }
        private short X { get; set; }
        private short Y { get; set; }
        private int frame { get; set; }

        public InputCommand(CommandExecuter executer, int id, string command)
        {
            this.executer = executer;
            this.id = id;
            var m = this.executer.parser.Match(command);

            this.direction = 0;
            if (m.Groups["direction"].Success)
            {
                this.direction = int.Parse(m.Groups["direction"].Value);
            }

            this.X = ConvertDirectionValue((this.direction % 3) - 2);
            this.Y = ConvertDirectionValue(((this.direction + 2) / 3) - 2);

            this.button = null;
            if (m.Groups["button"].Success && this.executer.assign.ContainsKey(m.Groups["button"].Value))
            {
                this.button = this.executer.assign[m.Groups["button"].Value];
            }

            this.frame = int.Parse(m.Groups["frame"].Value);

            this.flag = bool.Parse(m.Groups["flag"].Value);
        }

        override public void Execute(CommandExecuter executer)
        {
            executer.flag = this.flag;
            executer.vjoy.Reset();
            if (this.button != null)
            {
                executer.vjoy.SetButton(0, this.button.Value, true);
            }
            executer.vjoy.SetXAxis(0, this.X);
            executer.vjoy.SetYAxis(0, this.Y);
            this.executer.waitFrame = this.frame;
            executer.isKeyUpdated = true;
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

        public CommandExecuter() { }

        public void Initialize(Dictionary<string, int> assign)
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
            this.parser = new Regex(string.Format(@"(?<direction>[1-9]?)(?<button>[N{0}])\((?<frame>\d+)F,\s?(?<flag>true|false|True|False)\)", 
                string.Join("", (this.assign.Keys.ToArray()))));
        }

        public void Shutdown()
        {
            if (this.keyInputer != null && this.keyInputer.IsCompleted)
            {
                this.keyInputer.Wait();
            }
            this.vjoy.Shutdown();
        }

        public void CreateCommands(string commandString)
        {
            commandString.Split('>').Select((s) =>
            {
                if (this.parser.IsMatch(s))
                {
                    this.commands.Enqueue(new InputCommand(this, this.commands.Count, s));
                }
                return s;
            }).ToArray();
        }

        public void ExecuteCommandsEx()
        {
            this.keyInputer = Task.Factory.StartNew(() =>
            {
                this.isKeyInputing = true;
            })
            .ContinueWith((_) =>
            {
                this.commands.Select((c) =>
                {
                    c.Execute(this);
                    if (this.isKeyUpdated)
                    {
                        this.vjoy.Update(0);
                        Thread.Sleep(17 * this.waitFrame);
                    }
                    this.isKeyUpdated = false;
                    return c;
                }).ToArray();
            })
            .ContinueWith((_) =>
            {
                this.vjoy.Reset();
                this.vjoy.Update(0);
                Thread.Sleep(17);
                this.commands.Clear();
                this.isKeyInputing = false;
            });
        }

        public void Update()
        {
            if (this.waitFrame == 0)
            {
                if (this.commands.Any())
                {
                    this.commands.Dequeue().Execute(this);
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
