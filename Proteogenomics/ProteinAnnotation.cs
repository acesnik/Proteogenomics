﻿using Proteomics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UsefulProteomicsDatabases;

namespace Proteogenomics
{
    public static class ProteinAnnotation
    {
        /// <summary>
        /// Gets UniProt ptmlist
        /// </summary>
        /// <param name="spritzDirectory"></param>
        /// <returns></returns>
        public static List<ModificationWithLocation> GetUniProtMods(string spritzDirectory)
        {
            Loaders.LoadElements(Path.Combine(spritzDirectory, "elements.dat"));
            var psiModDeserialized = Loaders.LoadPsiMod(Path.Combine(spritzDirectory, "PSI-MOD.obo.xml"));
            return Loaders.LoadUniprot(Path.Combine(spritzDirectory, "ptmlist.txt"), Loaders.GetFormalChargesDictionary(psiModDeserialized)).ToList();
        }

        /// <summary>
        /// Transfers likely modifications from a list of proteins to another based on sequence similarity. Returns a list of new objects.
        /// </summary>
        /// <param name="proteogenomicProteins"></param>
        /// <param name="uniprotProteins"></param>
        /// <returns></returns>
        public static List<Protein> CombineAndAnnotateProteins(List<Protein> uniprotProteins, List<Protein> proteogenomicProteins)
        {
            List<Protein> newProteins = new List<Protein>();
            Dictionary<string, List<Protein>> dictUniprot = ProteinDictionary(uniprotProteins);
            Dictionary<string, List<Protein>> dictProteogenomic = ProteinDictionary(proteogenomicProteins);

            List<string> seqsInCommon = dictProteogenomic.Keys.Intersect(dictUniprot.Keys).ToList();
            List<string> pgOnlySeqs = dictProteogenomic.Keys.Except(dictUniprot.Keys).ToList();
            List<string> uniprotOnlySeqs = dictUniprot.Keys.Except(dictProteogenomic.Keys).ToList();

            foreach (string seq in seqsInCommon.Concat(pgOnlySeqs))
            {
                dictUniprot.TryGetValue(seq, out var uniprotProteinsSameSeq);
                dictProteogenomic.TryGetValue(seq, out var pgProteinsSameSeq);
                if (pgProteinsSameSeq == null) { continue; } // shouldn't happen

                var uniprot = CombineProteinsWithSameSeq(uniprotProteinsSameSeq); // may be null if a pg-only seq
                var pgProtein = CombineProteinsWithSameSeq(pgProteinsSameSeq);

                if (uniprot != null && uniprot.BaseSequence != pgProtein.BaseSequence) { throw new ArgumentException("not all proteins have the same sequence"); }

                // keep all sequence variations
                newProteins.Add(new Protein(
                    pgProtein.BaseSequence,
                    uniprot != null ? uniprot.Accession : pgProtein.Accession, //comma-separated
                    organism: uniprot != null ? uniprot.Organism : pgProtein.Organism,
                    name: pgProtein.Name, //comma-separated
                    full_name: uniprot != null ? uniprot.FullName : pgProtein.FullName, //comma-separated
                    isDecoy: pgProtein.IsDecoy,
                    isContaminant: pgProtein.IsContaminant,
                    sequenceVariations: pgProtein.SequenceVariations.OrderBy(v => v.OneBasedBeginPosition).ToList(),

                    // combine these
                    gene_names: (uniprot != null ? uniprot.GeneNames : new List<Tuple<string, string>>()).Concat(pgProtein.GeneNames).ToList(),

                    // transfer these
                    oneBasedModifications: uniprot != null ? uniprot.OneBasedPossibleLocalizedModifications : new Dictionary<int, List<Modification>>(),
                    proteolysisProducts: uniprot != null ? uniprot.ProteolysisProducts.ToList() : new List<ProteolysisProduct>(),
                    databaseReferences: uniprot != null ? uniprot.DatabaseReferences.ToList() : new List<DatabaseReference>(),
                    disulfideBonds: uniprot != null ? uniprot.DisulfideBonds.ToList() : new List<DisulfideBond>()
                ));
            }

            return newProteins;
        }

        /// <summary>
        /// Combine proteins containing the same sequence
        /// </summary>
        /// <param name="proteinsWithSameSequence"></param>
        /// <returns></returns>
        private static Protein CombineProteinsWithSameSeq(List<Protein> proteinsWithSameSequence)
        {
            if (proteinsWithSameSequence == null || proteinsWithSameSequence.Count <= 0)
            {
                return null; // no proteins to combine
            }

            var seq = new HashSet<string>(proteinsWithSameSequence.Select(p => p.BaseSequence));
            if (seq.Count > 1) { throw new ArgumentException("not all proteins have the same sequence"); }

            return new Protein(
                seq.First(),
                String.Join(",", proteinsWithSameSequence.Select(p => p.Accession)),
                organism: proteinsWithSameSequence.First().Organism,
                name: String.Join(",", proteinsWithSameSequence.Select(p => p.Name)),
                full_name: String.Join(",", proteinsWithSameSequence.Select(p => p.FullName)),
                isDecoy: proteinsWithSameSequence.All(p => p.IsDecoy),
                isContaminant: proteinsWithSameSequence.Any(p => p.IsContaminant),
                sequenceVariations: proteinsWithSameSequence.SelectMany(p => p.SequenceVariations).ToList(),
                gene_names: proteinsWithSameSequence.SelectMany(p => p.GeneNames).ToList(),
                oneBasedModifications: CollapseMods(proteinsWithSameSequence),
                proteolysisProducts: new HashSet<ProteolysisProduct>(proteinsWithSameSequence.SelectMany(p => p.ProteolysisProducts)).ToList(),
                databaseReferences: new HashSet<DatabaseReference>(proteinsWithSameSequence.SelectMany(p => p.DatabaseReferences)).ToList(),
                disulfideBonds: new HashSet<DisulfideBond>(proteinsWithSameSequence.SelectMany(p => p.DisulfideBonds)).ToList()
            );
        }

        /// <summary>
        /// Collapses modification dictionaries from multiple proteins into one
        /// </summary>
        /// <param name="proteins"></param>
        /// <returns></returns>
        private static Dictionary<int, List<Modification>> CollapseMods(IEnumerable<Protein> proteins)
        {
            Dictionary<int, HashSet<Modification>> result = new Dictionary<int, HashSet<Modification>>();
            if (proteins != null)
            {
                foreach (var kv in proteins.SelectMany(p => p.OneBasedPossibleLocalizedModifications).ToList())
                {
                    if (result.TryGetValue(kv.Key, out var modifications))
                    {
                        foreach (var mod in kv.Value) { modifications.Add(mod); }
                    }
                    else
                    {
                        result[kv.Key] = new HashSet<Modification>(kv.Value);
                    }
                }
            }
            return result.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        }

        /// <summary>
        /// Creates a sequence-to-proteins dictionary
        /// </summary>
        /// <param name="proteins"></param>
        /// <returns></returns>
        private static Dictionary<string, List<Protein>> ProteinDictionary(IEnumerable<Protein> proteins)
        {
            Dictionary<string, List<Protein>> dict = new Dictionary<string, List<Protein>>();
            foreach (Protein p in proteins)
            {
                if (dict.TryGetValue(p.BaseSequence, out var prots)) { prots.Add(p); }
                else { dict[p.BaseSequence] = new List<Protein> { p }; }
            }
            return dict;
        }

        /// <summary>
        /// Ensembl coding domain sequences (CDS) sometimes don't have start or stop codons annotated.
        /// The only way I can figure out how to tell which they are is to read in the protein FASTA and find the ones starting with X's or containing a stop codon '*'
        /// </summary>
        /// <param name="spritzDirectory"></param>
        /// <param name="proteinFastaPath"></param>
        /// <returns></returns>
        public static void GetImportantProteinAccessions(string proteinFastaPath, out Dictionary<string, string> proteinAccessionSequence, out HashSet<string> badProteinAccessions,
            out Dictionary<string, string> selenocysteineProteinAccessions)
        {
            Regex transcriptAccession = new Regex(@"(transcript:)([A-Za-z0-9_.]+)"); // need to include transcript accessions for when a GTF file is used and transcript IDs become the protein IDs
            List<Protein> proteins = ProteinDbLoader.LoadProteinFasta(proteinFastaPath, true, DecoyType.None, false,
                ProteinDbLoader.EnsemblAccessionRegex, ProteinDbLoader.EnsemblFullNameRegex, ProteinDbLoader.EnsemblFullNameRegex, ProteinDbLoader.EnsemblGeneNameRegex, null, out List<string> errors);
            proteinAccessionSequence = proteins.Select(p => new KeyValuePair<string, string>(p.Accession, p.BaseSequence))
                .Concat(proteins.Select(p => new KeyValuePair<string, string>(transcriptAccession.Match(p.FullName).Groups[2].Value, p.BaseSequence)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            HashSet<string> badOnes = new HashSet<string>(proteins.Where(p => p.BaseSequence.Contains('X') || p.BaseSequence.Contains('*'))
                .SelectMany(p => new string[] { p.Accession, transcriptAccession.Match(p.FullName).Groups[2].Value }));
            badProteinAccessions = badOnes;
            selenocysteineProteinAccessions = proteins.Where(p => !badOnes.Contains(p.Accession) && p.BaseSequence.Contains('U')).ToDictionary(p => p.Accession, p => p.BaseSequence);
        }
    }
}