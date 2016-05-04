ifeq ($(DEBUG),1)
	FLAGS=-d:DEBUG -debug
else
	FLAGS=
endif

Modules = .
SRC_DIR = $(addprefix src/, $(Modules))

ALL_SRC = $(foreach sdir, $(SRC_DIR), $(shell find $(sdir) -name '*.cs'))
EXCLUDES = gzip
SRC = $(filter-out $(EXCLUDES), $(ALL_SRC))
OBJ = bin/sim.exe

PHONY: build

build: $(OBJ)

$(OBJ): $(SRC)
	if [ ! -d bin ]; then mkdir bin; fi;
	dmcs -nowarn:0219 -r:Mono.Posix -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Data.dll -unsafe $(FLAGS) -out:$@ $^

clean:
	rm -rf $(OBJ) $(OBJ).mdb
