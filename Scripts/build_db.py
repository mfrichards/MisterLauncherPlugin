import sqlite3
import sys
import os

# Customize these variables to match LaunchBox and MiSTer installations.
mame_xml_file = r"C:\\LaunchBox\Metadata\MAME.xml"
db_file_name = r"C:\\LaunchBox\Metadata\arcade.db"
mister_share_path = r"\\mister\sdcard"
mister_actual_path = r"/media/fat"
db_batch_size = 20

# Folders to scan under the main "_Arcade" folder.
# Note: The "_Arcade" folder is always scanned first and those games
#       are marked as the "default" version.
# Note: After scanning "_Arcade" and "_alternatives", almost all games
#       found elsewhere (i.e. under "_Organized") will be duplicates.
folders_to_scan = [
    r"_alternatives",
    r"_Organized\_1 0-9",
    r"_Organized\_1 A-E",
    r"_Organized\_1 F-K",
    r"_Organized\_1 L-Q",
    r"_Organized\_1 R-T",
    r"_Organized\_1 U-Z",
    r"_Organized\_2 Region\_USA",
    r"_Organized\_2 Region\_Japan",
    r"_Organized\_2 Region\_World",
    r"_Organized\_2 Region\_Europe"
]

mame_game_count = 0
mister_mra_count = 0
mister_game_count = 0
mister_error_count = 0
mister_skip_count = 0

in_game = False
in_machine = False
game_setname = ""
game_name = ""
game_version = ""
game_desc = ""
game_year = 0
values = []
setnames = {}
game_names = set()
bootlegs = []
homebrews = []

def reset_game():
    global game_setname
    global game_name
    global game_version
    global game_desc
    global game_year
    
    game_setname = ""
    game_name = ""
    game_version = ""
    game_desc = ""
    game_year = 0

def get_value(line):
    start = line.index(">") + 1
    end = line.index("</", start)
    val = line[start:end].strip()
    return val.replace("&amp;", "&").replace("&quot;", "'")
    
def get_int_value(line):
    str_value = get_value(line)
    try:
        return int(str_value)
    except ValueError as e:
        return 0

def get_attribute(line, attr):
    token = f'{attr}="'
    try:
        start = line.index(token) + len(token)
        end = line.index('"', start)
        return line[start:end].strip()
    except ValueError as e:
        return ""

def db_execute(db, stmts):
    c = db.cursor()
    for stmt in stmts:
        c.execute(stmt)
    db.commit()
    
def db_update(db, sql, values):
    c = db.cursor()
    for val in values:
        c.execute(sql, val)
    db.commit()
    
def create_table(db):
    stmts = [
        """CREATE TABLE IF NOT EXISTS games (
            setname text PRIMARY KEY, 
            description text NOT NULL, 
            name text NOT NULL, 
            version text,
            year integer,
            path text,
            is_default integer default 0
        );""",
    ]
    db_execute(db, stmts)
    
def create_index(db):
    stmts = [ "CREATE INDEX i_name ON games(name);" ]
    db_execute(db, stmts)
    
def insert_data(db, data):
    sql = """INSERT INTO games(setname,description,name,version,year)
             VALUES(?,?,?,?,?)"""
    db_update(db, sql, data)
    
def normalize_name(name):
    name = name.lower()
    return name.replace("q'bert", "q*bert") \
               .replace("puck man", "pac-man") \
               .replace("puckman", "pac-man")
    
def normalize_path(path):
    return path.replace(mister_share_path, mister_actual_path).replace("\\", "/")
    
def process_game(db, setname, desc, name, version, year):
    global values
    global mame_game_count
    if (desc == ""):
        desc = f"{name} {version}".strip()
    elif (name == ""):
        parts = desc.split("(", 1)
        name = parts[0].strip()
        if (len(parts) > 1):
            version = f"({parts[1]}".strip()
    values.append((setname, desc, normalize_name(name), version, year))
    print(f"Game: {setname} -> {desc}")
    mame_game_count += 1
    if mame_game_count % db_batch_size == 0:
        insert_data(db, values)
        values = []

def update_game(db, setname, desc, name, version, year, path):
    global mister_game_count
    global mister_skip_count
    global setnames
    global game_names

    # Check if a path has already been found for this setname.
    existing = setnames.get(setname)
    if (existing):
        print(f"Skipping {path} -> found: {existing}")
        mister_skip_count += 1
        
    # Otherwise, go ahead and update the game record.
    else:
        name = normalize_name(name)
        path = normalize_path(path)
        is_default = 0

        select = "SELECT name FROM games WHERE setname=?"
        insert = "INSERT INTO games (setname,description,name,version,year,path,is_default) VALUES(?,?,?,?,?,?,?)"
        update = "UPDATE games SET path=?,is_default=? WHERE setname=?"

        c = db.cursor()
        c.execute(select, (setname,))
        existingRow = c.fetchone()
        if (existingRow):
            name = existingRow[0]
            is_default = 0 if name in game_names else 1
            c.execute(update, (path, is_default, setname))
        else:
            is_default = 0 if name in game_names else 1
            c.execute(insert, (setname, desc, name, version, year, path, is_default))
        db.commit()
        if (is_default == 1):
            game_names.add(name)
        print(f"Updated: {setname} -> {path}")
        mister_game_count += 1
        setnames[setname] = desc

def is_utf16(filename):
    with open(filename, 'rb') as f:
        start = f.read(2)
    return start in [b'\xff\xfe', b'\xfe\xff']

def process_mra_file(fullpath, fname, db):
    global mister_mra_count
    global mister_error_count
    setname = ""
    homebrew = False
    bootleg = False
    year = 0

    try:
        with open(fullpath, "r") as mra_file:
            for line in mra_file:
                line = line.strip()
                if (line.startswith("<setname>")):
                    setname = get_value(line)
                elif (line.startswith("<homebrew>")):
                    homebrew = (get_value(line).lower() == 'yes')
                elif (line.startswith("<bootleg>")):
                    bootleg = (get_value(line).lower() == 'yes')
                elif (line.startswith("<year>")):
                    year = get_int_value(line)
                elif (line.startswith("<rom")):
                    break

        if (len(setname) > 0):
            
            mister_mra_count += 1
            
            # Split name and version.
            index1 = fname.rfind(".")
            desc = fname[:index1]
            index2 = desc.find("(")
            name = desc[:index2].strip() if (index2 > 0) else desc
            version = desc[index2:].strip() if (index2 > 0) else ""

            # Save bootlegs and homebrews to process at the end.
            if (bootleg):
                bootlegs.append((setname, desc, name, version, year, fullpath))
            elif (homebrew):
                homebrews.append((setname, desc, name, version, year, fullpath))
            else:
                update_game(db, setname, desc, name, version, year, fullpath)

    except Exception as e:
        print(f"ERROR processing: {fullpath}")
        print(e)
        mister_error_count += 1

def process_others(db):
    global bootlegs
    global homebrews
    for game in bootlegs:
        update_game(db, game[0], game[1], game[2], game[3], game[4], game[5])
    bootlegs = []
    for game in homebrews:
        update_game(db, game[0], game[1], game[2], game[3], game[4], game[5])
    homebrews = []


print(">>> Processing Mame XML >>>")             

# XML files generated from mame.exe will be utf-16 encoded.
# LaunchBox Metadata\MAME.xml is utf-8 encoded.
file_enc = "utf-16" if is_utf16(mame_xml_file) else "utf-8"

with open(mame_xml_file, "r", encoding=file_enc) as mame_file:
    if os.path.exists(db_file_name):
        os.remove(db_file_name)
    with sqlite3.connect(db_file_name) as db:
        create_table(db)

        for line in mame_file:
            line = line.strip()
            if (line == "<MameFile>"):
                in_game = True
                reset_game()
            elif (line.startswith("<machine ")):
                in_machine = True
                reset_game()
                game_setname = get_attribute(line, "name")
            elif (in_game):
                if (line == "</MameFile>"):
                    in_game = False
                    process_game(db, game_setname, game_desc, game_name, game_version, game_year)
                elif (line.startswith("<FileName>")):
                    game_setname = get_value(line)
                elif (line.startswith("<Name>")):
                    game_name = get_value(line)
                elif (line.startswith("<Version>")):
                    game_version = get_value(line)
                elif (line.startswith("<Year>")):
                    game_year = get_int_value(line)
            elif (in_machine):
                if (line == "</machine>"):
                    in_machine = False
                    process_game(db, game_setname, game_desc, game_name, game_version, game_year)
                elif (line.startswith("<description>")):
                    game_desc = get_value(line)
                elif (line.startswith("<year>")):
                    game_year = get_int_value(line)

        if len(values) > 0:
            insert_data(db, values)
            
print(f">>> Found {mame_game_count} Mame games >>>")

print(">>> Processing Mister MRA files >>>")             

# Open database.
with sqlite3.connect(db_file_name) as db:
    
    # Process main _Arcade folder.
    arcade_path = f"{mister_share_path}\\_Arcade"
    for f in sorted(os.listdir(arcade_path)):
        fullpath = f"{arcade_path}\\{f}"
        if (os.path.isfile(fullpath) and fullpath.endswith(".mra")):
            process_mra_file(fullpath, f, db)
    process_others(db)

    # Process other folders.
    for root_folder in folders_to_scan:
        alt_path = f"{arcade_path}\\{root_folder}"
        for (root, dirs, files) in os.walk(alt_path, topdown=True, followlinks=True):
            dirs.sort()
            print(f'Scanning path: {root}')
            for f in sorted(files):
                fullpath = f"{root}\\{f}"
                if (os.path.isfile(fullpath) and fullpath.endswith(".mra")):
                    process_mra_file(fullpath, f, db)
            process_others(db)
            
    create_index(db);

print(f">>> Processed {mister_mra_count} MRA files and {mister_game_count} games >>>")
print(f">>> Skipped {mister_skip_count} duplicate games >>>")
print(f">>> Encountered {mister_error_count} error(s) reading MRA files >>>")
