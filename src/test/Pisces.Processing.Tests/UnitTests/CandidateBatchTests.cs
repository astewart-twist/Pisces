﻿using System.Collections.Generic;
using Pisces.Domain.Models.Alleles;
using Pisces.Domain.Types;
using Pisces.Processing.Models;
using Xunit;

namespace Pisces.Processing.Tests.UnitTests
{
    public class CandidateBatchTests
    {
        [Fact]
        public void CreateAndAdd()
        {
            var candidates = new List<CandidateAllele>()
            {
                new CandidateAllele("chr1", 100, "A", "AT", AlleleCategory.Insertion),
                new CandidateAllele("chr1", 100, "A", "A", AlleleCategory.Reference),
                new CandidateAllele("chr1", 100, "A", "T", AlleleCategory.Snv),
                new CandidateAllele("chr1", 200, "A", "T", AlleleCategory.Mnv)
            };

            var batch = new CandidateBatch(candidates);
            var batchCandidates = batch.GetCandidates();

            Assert.Equal(4, batchCandidates.Count);
            foreach(var candidate in candidates)
                Assert.True(batchCandidates.Contains(candidate));

            // add new candidate
            var newCandidate = new CandidateAllele("chr1", 200, "AT", "A", AlleleCategory.Deletion);
            batch.Add(new List<CandidateAllele>() { newCandidate });

            Assert.Equal(5, batchCandidates.Count);
            Assert.True(batchCandidates.Contains(newCandidate));
        }
    }
}
