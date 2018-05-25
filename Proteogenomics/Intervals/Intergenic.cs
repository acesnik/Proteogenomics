﻿using System.Collections.Generic;

namespace Proteogenomics
{
    public class Intergenic :
        Interval
    {
        public Gene LeftGene { get; set; }

        public Gene RightGene { get; set; }

        public Intergenic(Chromosome parent, string chromID, string source, string strand, long oneBasedStart, long oneBasedEnd, HashSet<Variant> variants)
            : base(parent, chromID, source, strand, oneBasedStart, oneBasedEnd, variants)
        {
        }

        public Intergenic(Intergenic intergenic) : base(intergenic)
        {
        }
    }
}