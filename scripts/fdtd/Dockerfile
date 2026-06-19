# Pinned open-source FDTD image for Lunima's "Recalculate S-Matrix": Meep (MPI)
# + gdsfactory + gplugins. Built on first use by DockerFdtdSMatrixService.
#
# Build context is the scripts/ directory (small) so the bridge script can be
# COPYed without shipping the whole repo to the Docker daemon:
#   docker build -f scripts/fdtd/Dockerfile -t lunima-meep:1 scripts
#
# Recipe notes (each cost real time to discover, see issue #569):
#  - Meep is conda-only (no PyPI, no native Windows) -> conda-forge.
#  - pymeep has no Python 3.12 build (only 3.11 / 3.13) -> pin python=3.13.
#  - Install into a SEPARATE env, not base: upgrading base mid-solve breaks
#    mamba's own libxml2 (exit 127).
#  - Pin the mpi_mpich build + mpich so Meep runs multi-core under mpirun
#    instead of the single-core 'nompi' default.
FROM condaforge/miniforge3:24.9.2-0

RUN mamba create -y -n mp -c conda-forge python=3.13 \
        "pymeep=*=mpi_mpich_*" pymeep-extras mpich \
    && mamba clean -afy

ENV PATH=/opt/conda/envs/mp/bin:$PATH

# Meep is not pip-installable, so do NOT use the gplugins[meep] extra.
RUN pip install --no-cache-dir "gdsfactory" "gplugins"

WORKDIR /work
COPY fdtd_sparams.py /work/fdtd_sparams.py
