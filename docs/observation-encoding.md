# Observation encoding v1

`sk.bot.ObservationEncoder` consumes only `GameObservation`. Both sample networks receive
the same 1,748 float32 features. The version is `schafkopf-observation-v1`; changing any
ordering or meaning requires a new version and regenerated demonstrations/checkpoints.
The encoder has no access to the live results DTO, hidden hands, seeds, payouts, or policies.

All seat-indexed features are relative to the observing seat: self, next clockwise,
opposite, previous clockwise. Cards retain engine IDs 0..31; suits are not permuted.

| Block | Offset | Length | Meaning |
| --- | ---: | ---: | --- |
| hand | 0 | 32 | Current own card presence |
| original_hand | 32 | 32 | Current hand plus this seat's public plays |
| phase | 64 | 5 | GameState one-hot |
| contract_type | 69 | 7 | GameType one-hot; None when absent; enum gaps reserved |
| contract_suit | 76 | 5 | Suit one-hot; None when absent |
| tout | 81 | 1 | Contract Tout flag |
| dealer | 82 | 4 | Relative seat one-hot |
| leader | 86 | 4 | Relative seat one-hot |
| active | 90 | 4 | Relative seat one-hot |
| declarer | 94 | 5 | Relative seat, or absent at index 4 |
| public_partner | 99 | 6 | Relative seat; unrevealed Ruf partner at 4; no Ruf contract at 5 |
| hand_counts | 105 | 4 | Relative seat order, divided by 8 |
| points | 109 | 4 | Captured points divided by 120 |
| trick_counts | 113 | 4 | Captured trick counts divided by 8 |
| current_trick | 117 | 132 | Four 33-way one-hots: absent at 0, card ID + 1 otherwise |
| trick_number | 249 | 1 | Completed tricks divided by 8 |
| ran_away | 250 | 1 | Public ran-away flag |
| played_by_seat | 251 | 128 | Four 32-card presence vectors from public history |
| own_role | 379 | 4 | Unknown, declarer, called-ace holder, defender |
| remaining_suits_and_trump | 383 | 5 | Nontrump counts Schellen/Herz/Gras/Eichel, then trump count, all / 8 |
| bidding_history | 388 | 176 | Eight slots: valid flag + relative actor one-hot (4) + action one-hot (17) |
| play_history | 564 | 1,184 | 32 slots: valid flag + relative actor one-hot (4) + card one-hot (32) |

The remaining-card classification uses the currently public contract, or normal Ruf/heart
trumps when no contract is known. Own role is unknown before a contract. For Ruf, an ace
holder can infer their role from their original hand while the public partner stays unknown.
A non-declarer without the called ace knows they are a defender, even if the other defender's
seat is unknown. Unused history slots contain only zeros. History is chronological within
each phase family and never truncated silently.

Legal actions are supplied separately as authoritative engine masks. The bidder uses local
IDs 0..16, identical to engine action IDs. The player uses card IDs 0..31; engine actions are
17 + local ID. Illegal logits are masked for both learning and inference. Batches without
legal actions, invalid labels, incompatible versions, or nonfinite outputs fail the run.

Demonstrations use bounded, checksummed binary shards of at most 4,096 rows. Each row is
little-endian float32[1748], uint32 legal bitmask, int32 local action, float32 terminal seat
payout / 100. All decisions of one complete game stay in the same seed split. Separate
JSONL traces contain seeds/actions/rewards for replay, never network input. Only one game
is buffered before attaching terminal payout labels. Training memory-maps shards and
materializes one minibatch at a time.

Tests pin a complete seed-42 opening vector, seat-rotation invariance, unseen-hand privacy,
absent/card-zero distinction, original-hand reconstruction, own Ruf roles, action adapters,
train/validation seed separation, and terminal labels reproduced by engine replay.
