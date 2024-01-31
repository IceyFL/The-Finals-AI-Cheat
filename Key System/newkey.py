from os.path import exists
import requests
import time

jsonblob_id = "" #json blob id
#get it from jsonblob.com
#use the following format
#{
#    "keys" : []
#}
#then press save

url = "https://jsonblob.com/api/"


def upload_key(json_id, key):
    headers = {"Content-Type": "application/json", "Accept": "application/json", "X-jsonblob": json_id}
    response = requests.get(url, headers=headers)

    if response.status_code == 200:
        data = response.json()
        if "keys" in data and key not in data["keys"]:
            data["keys"].append(key)
            response = requests.put(url, json=data, headers=headers)

            if response.status_code == 200:
                return True
            else:
                return "Error updating JSON blob"
        else:
            return False
    else:
        return "Error fetching JSON blob"


def keygen():
    new_key=time.time()
    a=int(input("Enter length of key in days: "))
    new_key=int(new_key+(a*86400))
    new_key=int(str(new_key)+str(a))
    return new_key


new_key = keygen()

result = upload_key(jsonblob_id, new_key)
if result is True:
    print(f"Key '{new_key}' added successfully.")
elif result is False:
    print(f"Key '{new_key}' already exists.")
else:
    print(f"Error: {result}")

input("Press Enter to exit")
