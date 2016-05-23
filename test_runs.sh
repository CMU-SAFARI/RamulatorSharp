#!/bin/sh
outdir=results
mkdir $outdir

###########################################
# 1 core -- run with 1 million instructions
###########################################

# ** Application: GCC **
# A baseline memory system similar to today's processor
mono bin/sim.exe -output $outdir/gcc.json -config configs/1core_base.cfg -workload workloads/1core 2
echo ""
# ChargeCache
mono bin/sim.exe -output $outdir/gcc+ChargeCache.json -config configs/1core_ChargeCache.cfg -workload workloads/1core 2
echo ""

# ** Application: Forkset (with copy commands) **
# A baseline memory system using memcpy
mono bin/sim.exe -output $outdir/forkset+memcpy.json -config configs/LISA_RISC_4core.cfg -N 1 -workload workloads/1core_copy 1 \
-mctrl.copy_method MEMCPY
echo ""
# LISA RISC (Chang et al., "Low-Cost Inter-Linked Subarrays (LISA): Enabling Fast Inter-Subarray Data Movement in DRAM", HPCA 2016)
mono bin/sim.exe -output $outdir/forkset+LISA-RISC.json -config configs/LISA_RISC_4core.cfg -N 1 -workload workloads/1core_copy 1 \
-mctrl.copy_method LISA_CLONE
echo ""
# LISA RISC+VILLA+LIP
mono bin/sim.exe -output $outdir/forkset+LISA-ALL.json -config configs/LISA_RISC+VILLA+LIP_4core.cfg -N 1 -workload workloads/1core_copy 1
echo ""

###########################################
# 4 cores -- run with 1 million instructions
###########################################

# ** Applications: forkset, bootup, tpcc64, libquantum
mono bin/sim.exe -output $outdir/4core+LISA-ALL.json -config configs/LISA_RISC+VILLA+LIP_4core.cfg -N 1 -workload workloads/4core_mix 1
echo ""
