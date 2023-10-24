using Chloe.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IpMonitor {

    [Table("IpLog")]
    internal class IpLog {

        [Column(IsPrimaryKey = true)]
        [AutoIncrement]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Type { get; set; }
        public DateTime? OpTime { get; set; }
    }
}
