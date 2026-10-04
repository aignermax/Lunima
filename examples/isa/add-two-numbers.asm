; add-two-numbers.asm — computes 7 + 6 and halts with ACC = 13.
; RAM[0] holds the first operand.
        LOAD 7
        STORE 0
        LOAD 6
        ADD 0
        HALT
