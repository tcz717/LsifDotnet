import sys
import json

if __name__ == "__main__":
    with open(sys.argv[1], "r") as f:
        last_id = 0
        total_violation = 0
        indexed = set()
        for line in f.readlines():
            item = json.loads(line)
            if item["id"] <= last_id:
                print("\033[33mWarn\33[0m: id not increasing:", item["id"])
                print("The line is: {line}, last id: {last_id}".format(line=line, last_id=last_id))
                total_violation += 1
            last_id = item["id"]

            if "inV" in item and not item["inV"] in indexed:
                print("\033[31mError\33[0m: inV not indexed:", item["inV"])
                print("The line is: {line}, last id: {last_id}".format(line=line, last_id=last_id))
                total_violation += 1
            if "outV" in item and not item["outV"] in indexed:
                print("\033[31mError\33[0m: outV not indexed:", item["outV"])
                print("The line is: {line}, last id: {last_id}".format(line=line, last_id=last_id))
                total_violation += 1
            if "inVs" in item and not indexed.issuperset(item["inVs"]):
                print("\033[31mError\33[0m: inVs not indexed:", item["inVs"])
                print("The line is: {line}, last id: {last_id}".format(line=line, last_id=last_id))
                total_violation += 1

            indexed.add(item["id"])

        print("Total violation:", total_violation)