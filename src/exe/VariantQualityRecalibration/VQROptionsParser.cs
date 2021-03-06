﻿using System.Collections.Generic;
using System.IO;
using Common.IO.Utility;
using CommandLine.Options;
using CommandLine.NDesk.Options;

namespace VariantQualityRecalibration
{
    public class VQROptionsParser : BaseOptionParser
    {
        public VQROptionsParser()
        {
            Options = new VQROptions();
        }

        public VQROptions VQROptions { get => (VQROptions) Options; }

        public override Dictionary<string, OptionSet> GetParsingMethods()
        {
            var requiredOps = new OptionSet
            {
                {
                    "vcf=",
                    OptionTypes.PATH + $" input file name",
                    value => VQROptions.InputVcf = value
                },
            };
            var commonOps = new OptionSet
            {

                {
                    "o|out|outfolder=",
                    OptionTypes.FOLDER + $"output directory",
                    value=> VQROptions.OutputDirectory = value
                },
                {
                    "locicount=",
                    OptionTypes.INT + $" When using a vcf instead of a genome.vcf, the user should input the estimated num loci",
                    value=> VQROptions.LociCount = int.Parse(value)
                },
                {
                    "b=",
                    OptionTypes.INT + $" baseline noise level, default { VQROptions.BaseQNoise}. (The new noise level is never recalibrated to lower than this.)",
                    value=> VQROptions.BaseQNoise = int.Parse(value)
                },
                {
                    "f=",
                    OptionTypes.INT + $" filter Q score, default { VQROptions.FilterQScore} (if a variant gets recalibrated, when we apply the \"LowQ\" filter)",
                    value=>VQROptions.FilterQScore = int.Parse(value)
                },
                {
                    "z=",
                    OptionTypes.FLOAT + $" thresholding parameter, default { VQROptions.ZFactor} (How many std devs above averge observed noise will the algorithm tolerate, before deciding a mutation type is likely to be artifact )",
                    value=> VQROptions.ZFactor = float.Parse(value)
                },
                {
                    "q=",
                    OptionTypes.INT + $" max Q score, default { VQROptions.MaxQScore} (if a variant gets recalibrated, when we cap the new Q score)",
                    value => VQROptions.MaxQScore = int.Parse(value)
                },
                {
                    "log=",
                    OptionTypes.STRING + $" log file name",
                    value=>VQROptions.LogFileName = value
                }

            };

            var optionDict = new Dictionary<string, OptionSet>
            {
                {OptionSetNames.Required,requiredOps},
                {OptionSetNames.Common,commonOps },
           };

            return optionDict;
        }

        public override void ValidateOptions()
        {
            //this would set an error code. Once we have one, we should quit.

            CheckInputFilenameExists(VQROptions.InputVcf, "vcf input", "--vcf");

            if (ParsingFailed)
                return;

            if (string.IsNullOrEmpty(Options.OutputDirectory))
            {
                Options.OutputDirectory = Path.GetDirectoryName(VQROptions.InputVcf);
            }

            CheckAndCreateDirectory(Options.OutputDirectory, " output directory", "-o", false);

            if (ParsingFailed)
                return;

            if (VQROptions.InputVcf.ToLower().EndsWith(".vcf") && !VQROptions.InputVcf.ToLower().EndsWith(".genome.vcf"))
            {
                HasRequiredParameter(VQROptions.LociCount, "the estimated num loci for vcf input", "--locicount");
            }

        }


    }
}