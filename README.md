## Ramulator#: A fast and lightweight DRAM simulator

Ramulator# (pronounced as ramulator sharp) is a fast and cycle-accurate DRAM
simulator that supports a wide array of commercial, as well as academic, DRAM
standards. It is a predecessor of
[Ramultor](https://github.com/CMU-SAFARI/ramulator). Therefore, the high-level
design Ramulator# is very similar to that of Ramulator. The main difference is
that it is implemented in the C# language whereas Ramulator is written in C++.
To understand how Ramulator# models a DRAM-based memory system, we refer the
readers to the original
[article](https://users.ece.cmu.edu/~omutlu/pub/ramulator_dram_simulator-ieee-cal15.pdf)
that provides further details on the design.

One main feature that is available in this version of the Ramulator is its
support for a recently proposed DRAM substrate, called [Low-Cost Inter-Linked
Subarrays
(LISA)](https://users.ece.cmu.edu/~kevincha/papers/kchang_lisa_hpca2016.pdf) by
Chang et al. in HPCA 2016.

## Getting Started

By default, Ramulator# uses [Mono](http://www.mono-project.com/) (an
open-source implementation of Microsoft's .NET framework) for compilation and
runtime on the installed system. It has been tested on Windows, Linux, and OS
X. Users can also choose to use Visual Studio as the development tool.

**Compilation**
`$ cd Ramulator#; make`

If the compilation completes without errors, the binary is located in `bin/sim.exe`.

**Running Ramulator#?**

To run some sample simulations, simply run
`$ ./test_runs.sh`

The script provides examples on running various system configurations, such as
a single-core system to a quad-core system with different memory configurations.
It generates results in the json format under the folder `results`.

At a high level, Ramulator# simulates a core running a single application by
consuming a trace. A trace consists of a list of memory requests and number of
CPU instructions generated from an actual application running on a real system.
We provide several sample traces collected from real applications running on a
real Linux system. They locate under `traces`. In a trace file, each line
represents a memory request with the following format:

`<num-cpuinst> <addr-read> <addr-writeback>`: The line has three tokens. The
first token represents the number of CPU (i.e., non-memory) instructions before
the memory request, and the second token is the decimal address of a *read*.
The third token is the decimal address of the *writeback* request, which is the
dirty cache-line eviction caused by the read request before it. Note the memory
requests in our traces refer to cache miss requests generated from the L2 cache.

Ramulator# consumes a workload file through the `-workload` flag. A workload
file contains a list of traces to run and a workload means a set of
applications to run on the system. Each line specifies a particular workload to
run, which can consist of one to many trace file names. The number of trace
files written on each line essentially represents the number of applications or
cores to run. For example, in `workloads/4core_mix`, the second line shows
`trace.forkset-rc.gz trace.bootup.1B-rc.gz tpcc64.gz 462.libquantum.gz`, which
has 4 different trace file names. This means that the simulator should be
configured with 4 cores (using `-N 4`) to consume these 4 traces. It is also
the first workload in the trace file. As the first line of the file specifies
the locations where the trace files locate. So by specifying `-workload
workloads/4core_mix 1`, the simulator simulates the first workload (line 2) in
the trace file.

## Contributors

    [Kevin Chang](https://users.ece.cmu.edu/~kevincha/)
    [Yoongu Kim](https://users.ece.cmu.edu/~yoonguk/)

## License

    Released under a BSD (3-clause) license
