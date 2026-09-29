; count-to-5.asm — counts from 0 to 5 and halts with ACC = 5.
; RAM[0] = counter, RAM[1] = 1 (increment), RAM[3] = 11 (= -5 mod 16, so the
; zero check ACC + 11 == 0 detects counter == 5 without a SUB instruction).
        LOAD 1
        STORE 1
        LOAD 11
        STORE 3
        LOAD 0          ; ACC = counter (starts at 0)
loop:   ADD 1           ; ACC = counter + 1
        STORE 0         ; counter = ACC
        ADD 3           ; ACC = counter - 5 (mod 16)
        JZ done
        LOAD 0          ; reload counter: ACC = 0 + RAM[0]
        ADD 0
        JMP loop
done:   LOAD 0          ; reload counter into ACC
        ADD 0
        HALT
