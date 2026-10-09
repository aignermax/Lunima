; mask-and-invert.asm — masks a value with AND, then inverts the result with NOT,
; and halts with ACC = 7. RAM[0] holds the mask 1010; AND keeps only the bits the
; mask lets through (1100 & 1010 = 1000 = 8) and NOT flips what is left
; (~1000 & 0xF = 0111 = 7). On the Logic Unit 4-bit chip both operations run on
; light: AND reads the Y0–Y3 taps, NOT the N0–N3 taps of the same network.
        LOAD 10         ; the mask 1010
        STORE 0         ; RAM[0] = mask
        LOAD 12         ; the value 1100
        AND 0           ; ACC = 1100 & 1010 = 1000 (8)
        NOT             ; ACC = ~1000 & 0xF = 0111 (7)
        STORE 1         ; keep the result in RAM[1]
        HALT
