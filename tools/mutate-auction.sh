#!/usr/bin/env bash
# Mutation testing for the housing auction. Each mutation is a specific,
# plausible way to get the mechanism wrong. A mutation that SURVIVES (check
# still passes) is a hole in the check, not a success.
set -u
cd "$(dirname "$0")/.."
A=src/CS2Econ.Core/HousingAuction.cs
T=src/CS2Econ.Core/EconTypes.cs
cp $A /tmp/A.bak; cp $T /tmp/T.bak
restore() { cp /tmp/A.bak $A; cp /tmp/T.bak $T; }

run() {
  local name="$1"
  # A mutation that fails to apply must NEVER be scored. M5 hard-coded the
  # old default (`= 4;`) in its search text; once the parameter moved to 12 the
  # replace silently did nothing and the unmutated build was recorded as
  # SURVIVED — twice, including once through a clean --no-incremental rebuild
  # that "confirmed" it. A survivor is the one result worth distrusting, so the
  # harness now proves the file actually changed before it believes one.
  if cmp -s "$A" /tmp/A.bak && cmp -s "$T" /tmp/T.bak; then
    echo "  $name: NOT APPLIED (mutation text did not match — result discarded)"; restore; return
  fi
  # --no-incremental is not optional here. sed-editing a file straight
  # after a restore leaves its mtime inside the same second, MSBuild decides
  # nothing changed, and the mutant is scored against the PREVIOUS binary. That
  # reported M5 as SURVIVED when a clean build kills it on 818 envious
  # households and an improving swap.
  dotnet build src/CS2Econ.Harness/CS2Econ.Harness.csproj -c Release --no-incremental -v q --nologo >/dev/null 2>&1
  if [ $? -ne 0 ]; then echo "  $name: BUILD FAILED (mutation invalid)"; restore; return; fi
  sleep 1
  out=$(timeout 900 dotnet run --project src/CS2Econ.Harness -c Release --no-build -- verify 2>&1 | grep "housing auction is a competitive")
  if echo "$out" | grep -q "\[PASS\]"; then
    echo "  $name: *** SURVIVED *** $(echo "$out" | cut -c1-200)"
  else
    echo "  $name: killed — $(echo "$out" | sed 's/.*— //' | cut -c1-150)"
  fi
  restore
}

echo "M1 no eviction (first-come-first-served, the defect this replaced)"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("                if (bid > weak)","                if (false && bid > weak)")
open(p,'w').write(s)
PY
run M1

echo "M2 raw willingness to pay (no competition adjustment)"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("""                double bid = Math.Min(bestVal - p.OutsideOption,
                                      bestVal - Math.Max(nextSur, p.OutsideOption) + eps);""",
            "                double bid = bestVal - p.OutsideOption;")
open(p,'w').write(s)
PY
run M2

echo "M3 price never rises above the reserve"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("                double post = Math.Max(Reserve[sub], Math.Max(rejected, Admitted[sub] - band));",
            "                double post = Reserve[sub];")
open(p,'w').write(s)
PY
run M3

echo "M4 capacity off by one (one more household than units)"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("                if (slot.Count < Capacity[bestSub])","                if (slot.Count < Capacity[bestSub] + 1)")
open(p,'w').write(s)
PY
run M4

echo "M5 no column generation (shortlist is final)"
python3 - <<'PY'
p='src/CS2Econ.Core/EconTypes.cs'; s=open(p).read()
import re
s2=re.sub(r"public int AuctionRepairRounds = \d+;","public int AuctionRepairRounds = 0;",s)
assert s2!=s, "M5 mutation did not apply"
s=s2
open(p,'w').write(s)
PY
run M5

echo "M6 outside option ignored (take any slot, however bad)"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("                if (bestSub < 0 || bestSur <= p.OutsideOption) { Assignment[i] = -1; Unassigned++; continue; }",
            "                if (bestSub < 0) { Assignment[i] = -1; Unassigned++; continue; }")
s=s.replace("""                double bid = Math.Min(bestVal - p.OutsideOption,
                                      bestVal - Math.Max(nextSur, p.OutsideOption) + eps);""",
            """                double bid = bestVal - Math.Max(nextSur, p.OutsideOption) + eps;""")
open(p,'w').write(s)
PY
run M6

echo "M7 shortlist ranked by cluster index, not by value"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("""                        if (v <= p.OutsideOption) continue;""",
            """                        if (v <= p.OutsideOption) continue;
                        v = -kc;   // MUTANT: rank by index""")
open(p,'w').write(s)
PY
run M7

echo "M8 stale prices: bidders read the reserve instead of the live price"
python3 - <<'PY'
p='src/CS2Econ.Core/HousingAuction.cs'; s=open(p).read()
s=s.replace("                        double sur = val - Price[sub];","                        double sur = val - Reserve[sub];")
open(p,'w').write(s)
PY
run M8

restore
dotnet build src/CS2Econ.Harness/CS2Econ.Harness.csproj -c Release --no-incremental -v q --nologo >/dev/null 2>&1
echo "restored"
