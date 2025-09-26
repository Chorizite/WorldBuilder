using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Models {
    public class DBDocument {
        [Key]
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime LastModified { get; set; }
    }
}
