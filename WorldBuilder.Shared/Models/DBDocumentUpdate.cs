using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Models {
    public class DBDocumentUpdate {
        [Key]
        public Guid Id { get; set; }
        public string DocumentId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; }
        public Guid ClientId { get; set; }
    }
}
