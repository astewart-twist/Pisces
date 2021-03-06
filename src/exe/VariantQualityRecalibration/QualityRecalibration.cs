﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pisces.IO.Sequencing;
using Pisces.Calculators;
using Pisces.Domain.Types;
using Common.IO.Utility;
using Common.IO;
using Pisces.IO;

namespace VariantQualityRecalibration
{
    public class QualityRecalibration
    {
        private const int VcfHeaderOffset = 4;
        public static void Recalibrate(string vcfIn, string countsFileIn, string outDir,
            int baselineQNoise, double zFactor, int maxQscore, int filterQScore,string quotedCmdLineString)
        {

            string vcfFileName = Path.GetFileName(vcfIn);
            string vcfOut = Path.Combine(outDir, vcfFileName + ".recal");

            if (File.Exists(vcfOut))
                File.Delete(vcfOut);

            try
            {
                DoRecalibrationWork(vcfIn, vcfOut, countsFileIn, baselineQNoise, zFactor, maxQscore, filterQScore,quotedCmdLineString);

                if (File.Exists(vcfOut))
                {
                    Logger.WriteToLog("The following vcf was recalibrated: " + vcfIn);
                }

            }
            catch (Exception ex)
            {
                Logger.WriteToLog("Recalibrate failed for " + vcfIn);
                Logger.WriteToLog("Exception: " + ex);
            }


        }

        private static void DoRecalibrationWork(string vcfIn, string vcfOut, string sampleCountsFileName, 
            int baselineQNoise, double zFactor, int maxQscore, int filterQScore,string quotedCommandLineString)
        {
            
            if (!File.Exists(sampleCountsFileName))
            {
                Logger.WriteToLog("Cannot recalibrate. Cannot find {0} ", sampleCountsFileName);
                return;
            }
            else
            {
                Logger.WriteToLog("Found counts file: {0} ", sampleCountsFileName);
            }

            var LookupTable = GetPhredScaledCalibratedRates(baselineQNoise, zFactor, sampleCountsFileName);

            //if no work to do here...
            if ((LookupTable == null) || (LookupTable.Count == 0))
            {
                Logger.WriteToLog("No recalibration needed.");
                return;
            }

            using (VcfReader reader = new VcfReader(vcfIn))
            using (StreamWriter writer = new StreamWriter(new FileStream(vcfOut, FileMode.CreateNew)))
            {
                writer.NewLine = "\n";
                List<string> headerLines = reader.HeaderLines;
                WriteHeaders(writer,headerLines, quotedCommandLineString);
                

                var originalVar = new VcfVariant();
                while (reader.GetNextVariant(originalVar))
                {

                    var cat = MutationCounter.GetMutationCategory(originalVar);

                    if (LookupTable.ContainsKey(cat))
                    {
                        UpdateVariant(maxQscore, filterQScore, LookupTable, originalVar, cat);
                    }
                    writer.WriteLine(originalVar);
                }

            }
        }

        private static void WriteHeaders(StreamWriter writer, List<string> headerLines, string quotedCommandLineString)
        {
            foreach (string headerLine in headerLines.Take(VcfHeaderOffset))
                writer.WriteLine(headerLine);

            var currentVersion = FileUtilities.LocalAssemblyVersion<QualityRecalibration>();
            writer.WriteLine("##VariantQualityRecalibration=VQR " + currentVersion);

            if (!string.IsNullOrEmpty(quotedCommandLineString))
                writer.WriteLine("##VQR_cmdline=" + quotedCommandLineString );

            for(var i=VcfHeaderOffset;i<headerLines.Count;i++)
                writer.WriteLine(headerLines[i]);

        }

        public static void UpdateVariant(int maxQscore, int filterQScore, Dictionary<MutationCategory, int> qCalibratedRates, VcfVariant originalVar, MutationCategory cat)
        {
            int depth;
            int callCount;

            bool canUpdateQ = HaveInfoToUpdateQ(originalVar, out depth, out callCount);

            if (canUpdateQ)
            {
                int newQ = VariantQualityCalculator.AssignPoissonQScore(
                    callCount, depth, qCalibratedRates[cat], maxQscore);

                InsertNewQ(qCalibratedRates, originalVar, cat, newQ);
               
                //update filters if needed
                if (newQ < filterQScore)            
                {
                    var vcfConfig = new VcfWriterConfig();
                    vcfConfig.VariantQualityFilterThreshold = filterQScore;
                    var formatter = new VcfFormatter(vcfConfig );
                    
                    string lowQString = formatter.MapFilter(FilterType.LowVariantQscore);
                    string passString = VcfFormatter.PassFilter;

                    if (originalVar.Filters.Contains(lowQString))
                        return;

                    if (originalVar.Filters == passString)
                        originalVar.Filters = lowQString;
                    else
                        originalVar.Filters += VcfFormatter.FilterSeparator + lowQString;

                }
            }
        }

        public static void InsertNewQ(Dictionary<MutationCategory, int> qCalibratedRates, VcfVariant originalVar, MutationCategory cat, int newQ)
        {
            originalVar.Quality = newQ;
            if (originalVar.Genotypes[0].ContainsKey("GQ"))
                originalVar.Genotypes[0]["GQ"] = newQ.ToString();
            if (originalVar.Genotypes[0].ContainsKey("GQX"))
                originalVar.Genotypes[0]["GQX"] = newQ.ToString();
            if (originalVar.Genotypes[0].ContainsKey("NL"))
                originalVar.Genotypes[0]["NL"] = qCalibratedRates[cat].ToString();
        }

        public static bool HaveInfoToUpdateQ(VcfVariant originalVar, out int depth, out int callCount)
        {
            bool canUpdateQ = false;
            depth = -1;
            callCount = -1;

            if ((originalVar.InfoFields == null) || (originalVar.Genotypes == null)
                || (originalVar.Genotypes.Count < 1))
                return false;

            if (originalVar.InfoFields.ContainsKey("DP"))
                canUpdateQ = int.TryParse(originalVar.InfoFields["DP"], out depth);

            if (originalVar.Genotypes[0].ContainsKey("AD"))
            {
                string[] spat = originalVar.Genotypes[0]["AD"].Split(',');

                if (spat.Length == 2)
                    canUpdateQ = (canUpdateQ && int.TryParse(spat[1], out callCount));
            }

            return canUpdateQ;
        }

        private static
            Dictionary<MutationCategory, int> GetPhredScaledCalibratedRates(int baselineQNoise, double zFactor, string noiseFile)
        {
            double baseNoiseRate = MathOperations.QtoP(baselineQNoise);
            var counts = new Counts();
            counts.LoadCountsFile(noiseFile);
            var countsByCategory = counts.CountsByCategory;
            var PhredScaledRatesByCategory = new Dictionary<MutationCategory, int>();

            countsByCategory.Remove(MutationCategory.Deletion);
            countsByCategory.Remove(MutationCategory.Insertion);
            countsByCategory.Remove(MutationCategory.Other);
            countsByCategory.Remove(MutationCategory.Reference);

            double[] sortedFinalCounts = countsByCategory.Values.OrderBy(d => d).ToArray();

            if (countsByCategory.Keys.Count != 12)
                return null;

            //take the average value, throwing out the top two and bottom two outlyiers.
            double numDataPoints = 8; //12 - 4;
            double avg = 0;
            for (int i = 2; i < 10; i++)
                avg += (sortedFinalCounts[i] / numDataPoints);

            //get the variance
            double variance = 0;
            for (int i = 2; i < 10; i++)
                variance += ((avg - sortedFinalCounts[i]) * (avg - sortedFinalCounts[i]) / numDataPoints);

            //threshold = avg + z * sigma
            double threshold = avg + zFactor * Math.Sqrt(variance);

            foreach (var cat in countsByCategory.Keys)
            {

                double mutationCount = countsByCategory[cat];

                if (mutationCount > threshold)
                {

                    //baseline noise level is 'b' .
                    //the observed transition-rate is how frequently we observe a vf >b.

                    //so our expected freq due to noise =
                    // (prob of observation f_i <= b) + (prob of observation f_i > b)

                    double observedNoiseRate = 0;
                    if (counts.NumPossibleVariants > 0)
                        observedNoiseRate = mutationCount / counts.NumPossibleVariants;

                    //deliberately taking floor instead of rounding.
                    PhredScaledRatesByCategory.Add(cat, (int)(MathOperations.PtoQ(observedNoiseRate + baseNoiseRate)));

                }
            }

            return PhredScaledRatesByCategory;

        }
    }
}
