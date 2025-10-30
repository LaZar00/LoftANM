# LoftANM

With this tool you can export and generate ANM files. These are the options of the tool:

<img width="1155" height="757" alt="imagen" src="https://github.com/user-attachments/assets/bcf3de05-c076-479b-959f-609e51c7b83f" />

You can:  
  
  -f/-F: Decode a full .ANM file as individual images, saved as .TGA files. All the frames.  
  -u/-U: Decode a full .ANM file as individual images, saved as .TGA files. Only Unique different palette color images.  
  -d/-D: Dump binary data of .ANM file as text file.  
  -i/-I: Import .TGA files (whose filenames are in IMPORT.TXT ANSI text file) in some <NAME>.ANM file.  
         * (It is mandatory that IMPORT.TXT exists)  
         * The new animation file would be called as <NAME>_NEW.ANM.  
  
          A sample IMPORT.TXT file could be:
          CINE00_0346_0000.TGA
          CINE00_0384_0000.TGA
