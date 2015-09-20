using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MultiShark.Protocols
{
    class BaseDefinition
    {
        public bool Outbound = false;
        public string Name = "";
        public bool Ignore = false;

        public override string ToString()
        {
            return "Name: " + Name + "; Outbound: " + Outbound + "; Ignored: " + Ignore;
        }
    }
}
