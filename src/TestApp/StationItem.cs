using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
    public class StationItem
    {
        public string Name { get; set; }
        public Uri Url { get; set; }
        public string RelativePath { get; internal set; } = ";";
    }
}
