; multiply-3x4.asm — computes 3 x 4 = 12 by repeated addition and halts with ACC = 12.
; The chip only knows ADD, so "multiply" is a loop: add 3 to a running total, four
; times. RAM[0] = running total, RAM[1] = 3 (the number we add), RAM[3] = 4, so the
; zero check ACC + 4 == 0 (mod 16) detects total == 12 without a SUB instruction —
; the same trick count-to-5.asm uses for its zero check. The total doubles as the
; loop counter: after exactly four ADDs of 3 it holds 12.
        LOAD 3
        STORE 1
        LOAD 4
        STORE 3
        LOAD 0          ; ACC = total (starts at 0)
loop:   ADD 1           ; ACC = total + 3 (3, 6, 9, 12 — one ADD per round)
        STORE 0         ; total = ACC
        ADD 3           ; ACC = total + 4 (mod 16) — zero exactly when total == 12
        JZ done
        LOAD 0          ; reload total: ACC = 0 + RAM[0]
        ADD 0
        JMP loop
done:   LOAD 0          ; reload the final total
        ADD 0           ; ACC = 12
        HALT
