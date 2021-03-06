﻿using System;
using System.Collections.Generic;
using System.Linq;
using Pisces.Domain.Models;
using Pisces.Domain.Models.Alleles;
using Pisces.Domain.Types;
using Pisces.Domain.Logic;
using Pisces.Domain.Utility;
using Pisces.Processing.Interfaces;
using Pisces.Processing.Models;
using MyRegionState = Pisces.Processing.RegionState.RegionState;

namespace Pisces.Processing.RegionState
{
    public class RegionStateManager : IStateManager
    {
        protected readonly Dictionary<int, MyRegionState> _regionLookup = new Dictionary<int, MyRegionState>(); 
        protected readonly int _regionSize = 1000;
        private int _minBasecallQuality;
        protected MyRegionState _lastAccessedBlock;
        protected Stack<MyRegionState> _reusableBlocks = new Stack<MyRegionState>();
        protected int _lastUpToBlockKey;
        private bool _includeRefAlleles;
        private ChrIntervalSet _intervalSet;
        private bool _trackOpenEnded;
        private bool _trackReadSummaries;
        private int? _readLength;
        private readonly int _numAnchorTypes;
        private int WellAnchoredIndex => _numAnchorTypes;
        private int NumAnchorIndexes => _numAnchorTypes * 2 + 1;

        public bool ExpectCollapsedReads { get; protected set; }
        public bool ExpectStitchedReads { get; protected set; }

        public RegionStateManager(bool includeRefAlleles = false, int minBasecallQuality = 20,
            bool expectStitchedReads = false,
            ChrIntervalSet intervalSet = null, int blockSize = 1000,
            bool trackOpenEnded = false, bool trackReadSummaries = false, int numAnchorTypes = 5)
        {
            _regionSize = blockSize;
            _minBasecallQuality = minBasecallQuality;
            _includeRefAlleles = includeRefAlleles;
            _intervalSet = intervalSet;
            _trackOpenEnded = trackOpenEnded;
            _trackReadSummaries = trackReadSummaries;
            ExpectStitchedReads = expectStitchedReads;
            ExpectCollapsedReads = false;
            _numAnchorTypes = numAnchorTypes;
        }

        public void AddCandidates(IEnumerable<CandidateAllele> candidateVariants)
        {
            foreach (var candidateVariant in candidateVariants)
            {
                var block = GetBlock(candidateVariant.ReferencePosition);
                block.AddCandidate(candidateVariant, _trackOpenEnded);
                if (candidateVariant.Type == AlleleCategory.Deletion || candidateVariant.Type == AlleleCategory.Insertion) {
                    block.AddCandidateGroup(candidateVariants);
                }
                
            }
        }

	    public double GetSumOfAlleleBaseQualities(int position, AlleleType alleleType, DirectionType directionType, int minAnchor = 0, int? maxAnchor = null, bool fromEnd = false, bool symmetric = false)
	    {
			var region = GetBlock(position, false);
	        return region == null ? 0 : region.GetSumOfAlleleBaseQualites(position, alleleType, directionType, minAnchor, maxAnchor, fromEnd);
        }

        public void AddGappedMnvRefCount(Dictionary<int, int> supportLookup)
        {
            foreach (var position in supportLookup.Keys)
            {
                var block = GetBlock(position);
                block.AddGappedMnvRefCount(position, supportLookup[position]);
            }
        }

        private int GetAnchorType(int alignmentEndPosition, int basePosition, int alignmentStartPosition)
        {
            // Get the minimum anchor (num mapped bases flanking this one in the read) by looking at both sides
            var leftAnchor = basePosition - alignmentStartPosition;
            var rightAnchor = alignmentEndPosition - basePosition;

            bool lessAnchoredOnRight = false;

            
            var minAnchor = int.MinValue;
            if (leftAnchor >= rightAnchor)
            {
                if (rightAnchor >= _numAnchorTypes)
                {
                    return WellAnchoredIndex;
                }

                minAnchor = NumAnchorIndexes - rightAnchor - 1;

            }
            else
            {
                if (leftAnchor >= _numAnchorTypes)
                {
                    return WellAnchoredIndex;
                }
                minAnchor = leftAnchor;
            }

            if (minAnchor < 0)
            {
                throw new ArgumentException(
                    $"Base position {basePosition} does not appear to be mapped in read ({alignmentStartPosition}-{alignmentEndPosition}).");
            }

            return minAnchor;
        }

        public void AddAlleleCounts(Read alignment)
        {
            if (!_readLength.HasValue)
                _readLength = alignment.ReadLength;

            var lastPosition = alignment.Position - 1;

            var cigarData = alignment.CigarData;

            var deletionLength = 0;
            var lengthBeforeDeletion = alignment.ReadLength;
            var endsInDeletion = cigarData.HasOperationAtOpIndex(0,'D',true);
            var endsInDeletionBeforeSoftclip = cigarData.HasOperationAtOpIndex(1,'D',true) && cigarData.HasOperationAtOpIndex(0,'S',true);

            if (endsInDeletion || endsInDeletionBeforeSoftclip)
            {
                deletionLength = (int) (endsInDeletionBeforeSoftclip ? cigarData[cigarData.Count -2].Length :
                    cigarData[cigarData.Count - 1].Length);
                lengthBeforeDeletion = (int) (endsInDeletionBeforeSoftclip ? alignment.ReadLength - cigarData[cigarData.Count -1].Length :  alignment.ReadLength);
            }

            var positionMapLength = alignment.PositionMap.Length;
            var alignmentEndPosition = alignment.EndPosition;
            var alignmentStartPosition = alignment.Position;

            for (var positionMapIndex = 0; positionMapIndex < positionMapLength; positionMapIndex++)
            {
                DirectionType directionType = alignment.SequencedBaseDirectionMap[positionMapIndex];


                if ((endsInDeletionBeforeSoftclip) && positionMapIndex==lengthBeforeDeletion)
                {
                    if (CandidateVariantFinder.CheckDeletionQuality(alignment, positionMapIndex, _minBasecallQuality))
                    {
                        for (var j = 1; j < deletionLength + 1; j++) // add any terminal deletion counts
                        {
                            var anchorIndex = NumAnchorIndexes - 1; // Last index
                            AddAlleleCount(j + lastPosition, AlleleType.Deletion, directionType, anchorIndex);
                            AddCollapsedReadCount(j + lastPosition, alignment, directionType);
                        }
                    }
                }

                var position = alignment.PositionMap[positionMapIndex];

                if (position == -1){
                    continue; // not mapped to reference
                }

                var anchorType = GetAnchorType(alignmentEndPosition, position, alignmentStartPosition);

                //if the deletion is of decent quality, add it to the counts matix
                if (CandidateVariantFinder.CheckDeletionQuality(alignment, positionMapIndex, _minBasecallQuality))
                {
                    for (var j = lastPosition + 1; j < position; j++) // add any deletion counts
                    {
                        AddAlleleCount(j, AlleleType.Deletion, directionType, anchorType);
                        AddCollapsedReadCount(j, alignment, directionType);
                    }
                }

                var alleleType = AlleleHelper.GetAlleleType(alignment.Sequence[positionMapIndex]);
                if (alignment.Qualities[positionMapIndex] < _minBasecallQuality)
                    alleleType = AlleleType.N; // record this event as a no call

                AddAlleleCount(position, alleleType, directionType, anchorType);
                if (alleleType != AlleleType.N)
                    AddCollapsedReadCount(position, alignment, directionType);
                AddAlleleBaseQuality(position, alleleType, directionType, Math.Pow(10, -1 * (int)alignment.Qualities[positionMapIndex] / 10f), anchorType);
                lastPosition = position;
            }

            if (endsInDeletion)
            {
                if (CandidateVariantFinder.CheckDeletionQuality(alignment, alignment.SequencedBaseDirectionMap.Length-1, _minBasecallQuality))
                {
                    for (var j = 1; j < deletionLength + 1; j++) // add any terminal deletion counts
                    {
                        DirectionType directionType =
                            alignment.SequencedBaseDirectionMap[alignment.SequencedBaseDirectionMap.Length - 1];

                        var anchorIndex = NumAnchorIndexes - 1; // Last index

                        AddAlleleCount(j + lastPosition, AlleleType.Deletion, directionType, anchorIndex); // ends in deletion -> within 1
                        AddCollapsedReadCount(j + lastPosition, alignment, directionType);
                    }
                }
            }

            // add coverage summary
            if (_trackReadSummaries)
            {
                var coverageSummary = alignment.GetCoverageSummary();
                var block = GetBlock(coverageSummary.ClipAdjustedEndPosition);
                    // store by end position so we can always be forward looking
                block.AddReadSummary(coverageSummary.ClipAdjustedEndPosition, coverageSummary);
            }
        }

		public int GetAlleleCount(int position, AlleleType alleleType, DirectionType directionType, int minAnchor = 0, int? maxAnchor = null, bool fromEnd = false, bool symmetric = false)
        {
            var region = GetBlock(position, false);
            return region == null ? 0 : region.GetAlleleCount(position, alleleType, directionType, minAnchor, maxAnchor, fromEnd, symmetric);
        }

        public List<ReadCoverageSummary> GetSpanningReadSummaries(int startPosition, int endPosition)
        {
            if (!_trackReadSummaries)
                throw new ArgumentException("Not configured to track read summaries.");

            var summaries = new List<ReadCoverageSummary>();

            for (var position = startPosition; position <= endPosition + (_readLength * 2); position++)  // look forward by a full possible stitch length
            {
                var region = GetBlock(position, false);

                if (region != null)
                {
                    var regionSummaries = region.GetReadSummaries(position);
                    if (regionSummaries != null)
                        summaries.AddRange(regionSummaries.Where(s => s.ClipAdjustedStartPosition <= endPosition && s.ClipAdjustedEndPosition >= startPosition));
                }
            }

            return summaries;
        }

        public int GetGappedMnvRefCount(int position)
        {
            var region = GetBlock(position, false);

            return region == null ? 0 : region.GetGappedMnvRefCount(position);
        }


        #region virtuals
        protected virtual void AddCollapsedReadCount(int position, Read alignment, DirectionType directionType)
        {
            // do nothing by default.
        }

        public virtual int GetCollapsedReadCount(int position, ReadCollapsedType directionType)
        {
            return 0;  // default behavior for non-collapsed and/or non-stitched BAM
        }

        /// <summary>
        /// Only pass back candidates from blocks where the entire block region is less than upToPosition.  
        /// The second criteria is to ensure variants that span blocks have fully completed info in either flanking block.
        /// </summary>
        /// <param name="upToPosition"></param>
        /// /// <param name="chrReference"></param>
        /// /// <param name="intervalSet"></param>
        /// <returns></returns>
        public virtual ICandidateBatch GetCandidatesToProcess(int? upToPosition, ChrReference chrReference = null,HashSet<Tuple<string,int,string,string>> forcesGtAlleles=null)
        {
            try
            {
                // only create a real batch if we haved moved onto another block 
                if (upToPosition.HasValue && GetBlockKey(upToPosition.Value) == _lastUpToBlockKey)
                {
                    return null;
                }

                var batch = new CandidateBatch {MaxClearedPosition = upToPosition.HasValue ? -1 : (int?) null};

                var blockKeys = upToPosition.HasValue
                    ? _regionLookup.Keys.Where(k => k *_regionSize <= upToPosition).ToArray()
                    : _regionLookup.Keys.ToArray();

                var blocks = new List<MyRegionState>();

                Array.Sort(blockKeys); // need to sort the keys so we can bounce out as soon as we hit a held block

                foreach (var key in blockKeys)
                {
                    var block = _regionLookup[key];
                    if (upToPosition != null && block.MaxAlleleEndpoint > upToPosition)
                    {
                        break;
                    }
	                //Console.WriteLine("block start="+ block.StartPosition+" ;block end = "+ block.EndPosition);
                    batch.Add(block.GetAllCandidates(_includeRefAlleles, chrReference, _intervalSet,forcesGtAlleles));
                    batch.BlockKeys.Add(key);
                    blocks.Add(block);
                }

                if (blocks.Any())
                {
                    batch.ClearedRegions = new List<Region>(blocks.Select(b => b as Region));
                    batch.MaxClearedPosition = blocks.Max(b => b.EndPosition);

                    if (upToPosition.HasValue && blocks.Max(b => b.MaxAlleleEndpoint) > batch.MaxClearedPosition.Value && _trackOpenEnded)
                        AddCollapsableFromOtherBlocks(batch,
                            batch.MaxClearedPosition.Value,
                            upToPosition.Value);
                }

                return batch;
            }
            finally
            {
                _lastUpToBlockKey = upToPosition.HasValue ? GetBlockKey(upToPosition.Value) : -1;
                // doesnt matter what we set to for last round
            }
        }

        public virtual void DoneProcessing(ICandidateBatch batch)
        {
            var candidateBatch = batch as CandidateBatch;

            if (candidateBatch != null)
            {
                foreach (var key in candidateBatch.BlockKeys)
                {
                    var blockToRemove = _regionLookup[key];
                    _reusableBlocks.Push(blockToRemove); // save for reuse later

                    _regionLookup.Remove(key);

                    if (_lastAccessedBlock != null && _lastAccessedBlock.Equals(blockToRemove))
                        _lastAccessedBlock = null;
                }
            }
        }

        protected virtual RegionState CreateBlock(int startPosition, int endPosition)
        {
            return new MyRegionState(startPosition, endPosition, _numAnchorTypes);
        }
        #endregion // virtual

        protected MyRegionState GetBlock(int position, bool addIfMissing = true)
        {
            if (position <= 0)
                throw new ArgumentException("Position must be greater than 0.");

            if (_lastAccessedBlock != null && _lastAccessedBlock.ContainsPosition(position))  // performance improvement to remember last block
                return _lastAccessedBlock;

            var blockKey = GetBlockKey(position);

            MyRegionState block;
            if (!_regionLookup.TryGetValue(blockKey, out block))
            {
                if (!addIfMissing)
                    return null;

                block = CreateOrReuseBlock((blockKey - 1) * _regionSize + 1, blockKey * _regionSize);
                _regionLookup[blockKey] = block;
            }

            _lastAccessedBlock = block;
            return block;
        }

        protected int GetBlockKey(int position)
        {
            // 1-1000 -> 1st block
            // 1001-2000 -> 2nd block
            // 2001-3000 -> 3rd block
            return (int)Math.Ceiling((double)position / _regionSize);
        }

        #region private 

        private void AddAlleleCount(int position, AlleleType alleleType, DirectionType directionType, int anchorType)
        {
            var block = GetBlock(position);
            block.AddAlleleCount(position, alleleType, directionType, anchorType);
        }

        private void AddAlleleBaseQuality(int position, AlleleType alleleType, DirectionType directionType, double baseQuality, int anchorType)
        {
            var block = GetBlock(position);
            block.AddBaseQualites(position, alleleType, directionType, baseQuality, anchorType);
        }
        /// <summary>
        /// Get block by either reusable one that is available for reuse, or creating a new one if non available for reuse.
        /// </summary>
        /// <param name="startPosition"></param>
        /// <param name="endPosition"></param>
        /// <returns></returns>
        private MyRegionState CreateOrReuseBlock(int startPosition, int endPosition)
        {
            MyRegionState block;

            if (_reusableBlocks.Any())
            {
                block = _reusableBlocks.Pop();
                block.Reset(startPosition, endPosition);
            }
            else
            {
                block = CreateBlock(startPosition, endPosition);
            }
            return block;
        }

        private void AddCollapsableFromOtherBlocks(ICandidateBatch batch, int maxClearedPosition, int upToPosition)
        {
            // grab all right anchored collapsable variants between max cleared and up to position and add to batch.
            // this really only applies to snv/mnvs.  collapsable insertions always have the same coordinate.
            // if these variants dont collapse, they get added back to state manager later

            foreach (var block in _regionLookup.Values)
            {
                if (block.StartPosition > maxClearedPosition && block.StartPosition <= upToPosition)
                {
                    var collapsableVariants = block.ExtractCollapsable(upToPosition);

                    foreach (var collapsableVariant in collapsableVariants)
                        batch.Add(collapsableVariant);
                }
            }
        }

        #endregion

    }
}
