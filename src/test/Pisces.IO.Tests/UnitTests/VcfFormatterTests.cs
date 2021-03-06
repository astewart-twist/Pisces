﻿using System.Collections.Generic;
using Pisces.Domain.Models.Alleles;
using Pisces.Domain.Types;
using TestUtilities;
using Xunit;


namespace Pisces.IO.Tests.UnitTests
{
    public class VcfFormatterTests
    {
        VcfFormatter _formatter;
        CalledAllele _v1;
        CalledAllele _v2;
        CalledAllele _v3;
        int _estimatedBaseCallQuality = 23;

        public void Initialize()
        {

            VcfWriterConfig config = new VcfWriterConfig
                    {
                        DepthFilterThreshold = 500,
                        VariantQualityFilterThreshold = 20,
                        StrandBiasFilterThreshold = 0.5f,
                        FrequencyFilterThreshold = 0.007f,
                        MinFrequencyThreshold = 0.007f,
                        ShouldOutputNoCallFraction = true,
                        ShouldOutputStrandBiasAndNoiseLevel = true,
                        ShouldFilterOnlyOneStrandCoverage = true,
                        EstimatedBaseCallQuality = _estimatedBaseCallQuality,
                        //AllowMultipleVcfLinesPerLoci = true
                    };

            _formatter = new VcfFormatter(config);

            _v1 = TestHelper.CreatePassingVariant(false);
            _v2 = TestHelper.CreatePassingVariant(false);
            _v3 = TestHelper.CreatePassingVariant(false);
        }

        [Fact]
        [Trait("ReqID", "SDS-VCF-9-FILTER")]
        public void FilterMerge()
        {
            Initialize();

            _v1.Filters = new List<FilterType> { FilterType.LowDepth, FilterType.LowVariantQscore};
            _v2.Filters = new List<FilterType> {FilterType.MultiAllelicSite };
            _v3.Filters = new List<FilterType> {FilterType.LowDepth };

            var mergedFilters = VcfFormatter.MergeFilters(new List<CalledAllele> { _v1, _v2, _v3 });
            var expectedFilters = new List<FilterType> { FilterType.LowDepth, FilterType.LowVariantQscore, FilterType.MultiAllelicSite };
            Assert.Equal(expectedFilters, mergedFilters);
     
        }


        [Fact]
        [Trait("ReqID", "SDS-VCF-9-REF-and-ALT")]
        public void ReferenceMerge()
        {
            Initialize();

            _v1.ReferenceAllele = "CA";
            _v2.ReferenceAllele = "CAA";

            _v1.AlternateAllele = "C";
            _v2.AlternateAllele = "C";

            string[] mergedRefAndAlt = _formatter.MergeCrushedReferenceAndAlt(new List<CalledAllele> { _v1, _v2 });

            string expectedReference = "CAA";
            string expectedAlt = "CA,C";

            Assert.Equal(expectedReference, mergedRefAndAlt[0]);
            Assert.Equal(expectedAlt, mergedRefAndAlt[1]);

            _v1.ReferenceAllele = "C";
            _v2.ReferenceAllele = "CAA";

            _v1.AlternateAllele = "CA";
            _v2.AlternateAllele = "C";

            mergedRefAndAlt = _formatter.MergeCrushedReferenceAndAlt(new List<CalledAllele> { _v1, _v2 });

            expectedReference = "CAA";
            expectedAlt = "CAAA,C";

            Assert.Equal(expectedReference, mergedRefAndAlt[0]);
            Assert.Equal(expectedAlt, mergedRefAndAlt[1]);

            _v1.ReferenceAllele = "C";
            _v2.ReferenceAllele = "C";

            _v1.AlternateAllele = "CA";
            _v2.AlternateAllele = "CAA";

            mergedRefAndAlt = _formatter.MergeCrushedReferenceAndAlt(new List<CalledAllele> { _v1, _v2 });

            expectedReference = "C";
            expectedAlt = "CA,CAA";

            Assert.Equal(expectedReference, mergedRefAndAlt[0]);
            Assert.Equal(expectedAlt, mergedRefAndAlt[1]);

            _v1.ReferenceAllele = "C";
            _v2.ReferenceAllele = "C";

            _v1.AlternateAllele = ".";
            _v2.AlternateAllele = "T";

            mergedRefAndAlt = _formatter.MergeCrushedReferenceAndAlt(new List<CalledAllele> { _v1, _v2 });

            expectedReference = "C";
            expectedAlt = ".,T";

            Assert.Equal(expectedReference, mergedRefAndAlt[0]);
            Assert.Equal(expectedAlt, mergedRefAndAlt[1]);
        }

        [Fact]
        [Trait("ReqID", "SDS-??")]
        public void MergeFromBug185()
        {
            Initialize();
            _v1.ReferenceAllele = "A";
            _v2.ReferenceAllele = "AC";
            _v3.ReferenceAllele = "ACGTTT";

            _v1.AlternateAllele = "C";
            _v2.AlternateAllele = "A";
            _v3.AlternateAllele = "A";


            string[] mergedRefAndAlt = _formatter.MergeCrushedReferenceAndAlt(new List<CalledAllele> { _v1, _v2, _v3 });

            string expectedReference = "ACGTTT";
            string expectedAlt = "CCGTTT,AGTTT,A";

            Assert.Equal(expectedReference, mergedRefAndAlt[0]);
            Assert.Equal(expectedAlt, mergedRefAndAlt[1]);
        }


        [Fact]
        [Trait("ReqID", "SDS-??")]
        public void AltMerge()
        {
            Initialize();

            _v1.ReferenceAllele = "A";
            _v2.ReferenceAllele = "A";
            _v3.ReferenceAllele = "A";

            _v1.AlternateAllele = "C";
            _v2.AlternateAllele = ".";
            _v3.AlternateAllele = "ACGTTT";

            string[] mergedRefAndAlt = _formatter.MergeCrushedReferenceAndAlt(new List<CalledAllele> { _v1, _v2, _v3 });

            //string expectedReference = "A,A,A";
            string expectedReference = "A";
            string expectedAlt = "C,.,ACGTTT";
            Assert.Equal(expectedReference, mergedRefAndAlt[0]);
            Assert.Equal(expectedAlt, mergedRefAndAlt[1]);

        }

        [Fact]
        [Trait("ReqID", "SDS-VCF-9-QUAL")]
        public void QMerge()
        {
            Initialize();

            _v1.VariantQscore = 200;
            _v2.VariantQscore = 20;
            _v3.VariantQscore = 50;

            int mergedQ = _formatter.MergeVariantQScores(new List<CalledAllele> { _v1, _v2, _v3 });
             Assert.Equal(20, mergedQ);

        }

        [Fact]
        [Trait("ReqID", "SDS-VCF-9-INFO-and-FORMAT")]
        public void InfoAndFormatMerge()
        {
            Initialize();

            CalledAllele _v0 = TestHelper.CreatePassingVariant(true);

            _v0.GenotypeQscore = 42;
            _v1.GenotypeQscore = 200;
            _v2.GenotypeQscore = 20;
            _v3.GenotypeQscore = 50;

            _v1.AlleleSupport = 10;
            _v2.AlleleSupport = 20;
            _v3.AlleleSupport = 30;

            _v1.TotalCoverage = 100;
            _v2.TotalCoverage = 100;
            _v3.TotalCoverage = 100;

            _v0.NoiseLevelApplied = _estimatedBaseCallQuality;
            _v1.NoiseLevelApplied = _estimatedBaseCallQuality;
            _v2.NoiseLevelApplied = _estimatedBaseCallQuality;
            _v3.NoiseLevelApplied = _estimatedBaseCallQuality;


            _v1.Genotype = Genotype.HomozygousRef;
            string[] oneVariantTest = _formatter.ConstructFormatAndSampleString(new List<CalledAllele> { _v0, }, 490);

            _v1.Genotype = Genotype.HeterozygousAltRef;
            string[] twoVariantTestAltRef = _formatter.ConstructFormatAndSampleString(new List<CalledAllele> { _v1 }, 63);

            _v1.Genotype = Genotype.HeterozygousAlt1Alt2;
            string[] twoVariantTestAlt1Alt2 = _formatter.ConstructFormatAndSampleString(new List<CalledAllele> { _v1, _v2, }, 65);
            
            string[] threeVariantTest = _formatter.ConstructFormatAndSampleString(new List<CalledAllele> { _v1, _v2, _v3 }, 78);

            string expectedFormat = "GT:GQ:AD:DP:VF:NL:SB:NC";
            string expectedRefSample = "0/0:42:490:490:0.0000:23:0.0000:0.0000";
            string expectedAltRefSample = "0/1:200:0,10:63:0.1000:23:0.0000:0.0000";
            string expectedAlt1Alt2Sample = "1/2:20:10,20:65:0.4615:23:0.0000:0.0000";//0.4615=30/65
            string expected3VarSample = "1/2:20:10,20,30:78:0.7692:23:0.0000:0.0000"; //0.7692 = (10+20+30)/78

            Assert.Equal(expectedFormat, oneVariantTest[0]);
            Assert.Equal(expectedRefSample, oneVariantTest[1]);

            Assert.Equal(expectedFormat, twoVariantTestAltRef[0]);
            Assert.Equal(expectedAltRefSample, twoVariantTestAltRef[1]);

            Assert.Equal(expectedFormat, twoVariantTestAlt1Alt2[0]);
            Assert.Equal(expectedAlt1Alt2Sample, twoVariantTestAlt1Alt2[1]);

            Assert.Equal(expectedFormat, threeVariantTest[0]);
            Assert.Equal(expected3VarSample, threeVariantTest[1]);

        }

        [Fact]
        public void GenoytyeOthersRepresentedCorrectly()
        {
            var vcfFormatter = new VcfFormatter();
            Assert.Equal("2/2",vcfFormatter.MapGenotype(Genotype.Others));
        }

        [Fact]
        public void ForcedAlleleWithGenotypeOtherGetCorrectAltAlleles()
        {
            Initialize();
            var forcedCallAllele = new CalledAllele(AlleleCategory.NonReference)
            {
                ReferencePosition = 123,
                Chromosome = "chr1",
                GenotypeQscore = 0,
                ReferenceAllele = "A",
                AlternateAllele = "T",
                TotalCoverage = 50,
                AlleleSupport = 0,
                ReferenceSupport = 20,
                IsForcedToReport = true,
                Genotype = Genotype.Others
            };
            var observedAlleles = _formatter.SetUncrushedReferenceAndAlt(forcedCallAllele);
            string[] sampleField = _formatter.ConstructFormatAndSampleString(new List<CalledAllele> { forcedCallAllele }, 50);
            Assert.Equal("T,<M>",observedAlleles[1]);
            string expectedFormat = "GT:GQ:AD:DP:VF:NL:SB:NC";
            string expectedSample = "2/2:0:20,0,30:50:0.0000:0:0.0000:0.0000";

            Assert.Equal(expectedFormat, sampleField[0]);
            Assert.Equal(expectedSample, sampleField[1]);
        }
    }
}
