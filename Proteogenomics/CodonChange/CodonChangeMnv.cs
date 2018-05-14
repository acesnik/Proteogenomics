﻿using Bio;
using Bio.Extensions;
using System;
using System.Text;

namespace Proteogenomics
{
    public class CodonChangeMnv
        : CodonChange
    {
        private long cdsStart;
        private long cdsEnd;

        public CodonChangeMnv(Variant variant, Transcript transcript, VariantEffects variantEffects)
            : base(variant, transcript, variantEffects)
        {
            ReturnNow = false;
            RequireNetCdsChange = true;
        }

        private long cdsBaseNumber(long pos, bool usePrevBaseIntron)
        {
            if (pos < cdsStart)
            {
                return Transcript.IsStrandPlus() ? 0 : Transcript.RetrieveCodingSequence().Count - 1;
            }
            if (pos > cdsEnd)
            {
                return Transcript.IsStrandPlus() ? Transcript.RetrieveCodingSequence().Count - 1 : 0;
            }
            return Transcript.BaseNumberCds(pos, usePrevBaseIntron);
        }

        /// <summary>
        /// Calculate a list of codon changes
        /// </summary>
        public override void ChangeCodon()
        {
            codonOldNew();

            // Create change effect
            Effect(Transcript, EffectType.CODON_CHANGE, true); // Use a generic low priority variant, this allows 'setCodons' to override it
        }

        /// <summary>
        /// Calculate codons old / codons new
        /// </summary>
        protected void codonOldNew()
        {
            if (!Transcript.Intersects(Variant)) { return; }

            // CDS coordinates
            cdsStart = Transcript.IsStrandPlus() ? Transcript.CdsOneBasedStart : Transcript.CdsOneBasedEnd;
            cdsEnd = Transcript.IsStrandPlus() ? Transcript.CdsOneBasedEnd : Transcript.CdsOneBasedStart;

            // Does it intersect CDS?
            if (cdsStart > Variant.OneBasedEnd) { return; }
            if (cdsEnd < Variant.OneBasedStart) { return; }

            // Base number relative to CDS start
            long scStart, scEnd;
            if (Transcript.IsStrandPlus())
            {
                scStart = cdsBaseNumber(Variant.OneBasedStart, false);
                scEnd = cdsBaseNumber(Variant.OneBasedEnd, true);
            }
            else
            {
                scEnd = cdsBaseNumber(Variant.OneBasedStart, true);
                scStart = cdsBaseNumber(Variant.OneBasedEnd, false);
            }

            // Update coordinates
            CodonStartNumber = (int)(scStart / CODON_SIZE);
            CodonStartIndex = (int)(scStart % CODON_SIZE);

            // MNP overlap in coding part
            long scLen = scEnd - scStart;
            if (scLen < 0) return;

            // Round to codon position
            long scStart3 = round3(scStart, false);
            long scEnd3 = round3(scEnd, true);
            long scLen3 = scEnd3 - scStart3;
            if (scEnd3 == scStart3) { scEnd3 += 3; }// At least one codon

            // Append 'N'
            string padN = "";
            long diff = scEnd3 - (Transcript.RetrieveCodingSequence().Count - 1);
            if (diff > 0)
            {
                scEnd3 = Transcript.RetrieveCodingSequence().Count - 1;
                // Pad with 'N'
                switch (diff)
                {
                    case 1:
                        padN = "N";
                        break;

                    case 2:
                        padN = "NN";
                        break;

                    default:
                        throw new ArgumentOutOfRangeException("Sanity check failed. Number of 'N' pading is :" + diff + ". This should not happen!");
                }
            }

            // Get old codon (reference)
            CodonsReference = SequenceExtensions.ConvertToString(Transcript.RetrieveCodingSequence().GetSubSequence(scStart3, scLen3));

            // Get new codon (change)
            string prepend = CodonsReference.Substring(0, (int)(scStart - scStart3));
            string append = scEnd3 > scEnd ? CodonsReference.Substring(CodonsReference.Length - (int)(scEnd3 - scEnd)) : "";
            CodonsAlternate = prepend + NetCdsChange() + append;

            // Pad codons with 'N' if required
            CodonsReference += padN;
            CodonsAlternate += padN;

            //---
            // Can we simplify codons?
            //---
            if ((CodonsReference != null) && (CodonsAlternate != null))
            {
                while ((CodonsReference.Length >= 3) && (CodonsAlternate.Length >= 3))
                {
                    // First codon
                    string cold = CodonsReference.Substring(0, 3);
                    string cnew = CodonsAlternate.Substring(0, 3);

                    // Are codons equal? => Simplify
                    if (cold.Equals(cnew, StringComparison.InvariantCultureIgnoreCase))
                    {
                        CodonsReference = CodonsReference.Substring(3);
                        CodonsAlternate = CodonsAlternate.Substring(3);
                        CodonStartNumber++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        protected override string CodonsAlt()
        {
            return CodonsAlternate;
        }

        /// <summary>
        /// Calculate old codons
        /// </summary>
        /// <returns></returns>
        protected override string CodonsRef()
        {
            return CodonsReference;
        }

        /// <summary>
        /// We may have to calculate 'netCdsChange', which is the effect on the CDS.
        /// Note: A deletion or a MNP might affect several exons
        /// </summary>
        /// <returns></returns>
        protected override string NetCdsChange()
        {
            if (Variant.Length() > 1)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Exon exon in Transcript.ExonsSortedStrand)
                {
                    string seq = Variant.NetChange(exon);
                    sb.Append(exon.IsStrandPlus() ? seq : SequenceExtensions.ConvertToString(new Sequence(Alphabets.AmbiguousDNA, seq).GetReverseComplementedSequence()));
                }
                return sb.ToString();
            }

            return Variant.NetChange(Transcript.IsStrandPlus());
        }

        private static long round3(long num, bool end)
        {
            return end ?
                num % 3 == 2 ? num : (num / 3) * 3 + 2 :
                num % 3 == 0 ? num : (num / 3) * 3;
        }
    }
}