package genshin_piano;

import java.io.*;

public class readMe {
    readMe(File file) {
        BufferedWriter bw;
        String jarName = "Start GenshinPiano";
        try {
            bw = new BufferedWriter(new FileWriter(file + "\\README.txt"));
            bw.write("""
                    ##################
                    #    请不要删除此文件    #
                    ##################
                    @author: 冰焰_Frozen
                    https://space.bilibili.com/507499226
                    赞助作者:
                    爱发电：https://afdian.net/@Frozen_Flame
                    运行该jar包需要java环境（如果在此计算机上能运行minecraft，请忽略此项）
                    第一次运行必定会直接退出 并生成debug.bat和README.txt等文件
                    在此之后的运行都会正常
                    !不要删除生成文件！

                    ！请有非分之想的人自重！
                    
                    建议双击"""+jarName+
                    """
                    .exe运行程序，这样可以以管理员身份运行（游戏中使用必须用管理员身份）。
                    exe文件可以固定到开始菜单 方便运行
                    .bat用于调试，不建议用bat运行
                    更不建议直接运行jar
                    
                    运行中点击面板（使面板focused）即可停止弹奏
                    再次弹奏请点击停止弹奏后再开始弹奏
                    
                    有问题请私信up
                    更多曲谱、分享曲谱请访问https://frozenflame.top

                    经不完全测试，在未使用管理员身份情况下不能在游戏内进行按键操作（yuanshen.exe以管理员身份运行）

                    特此声明：本程序不含任何恶意代码
                    欢迎提交bug和建议
                    """);
            bw.flush();
            bw.close();
        } catch (IOException var4) {
            var4.printStackTrace();
            System.out.println("上面一坨的意思是生成README.txt失败，多半是路径问题\n不影响正常使用");
        }

        if ((new File(file + "\\debug.bat")).exists()&&(new File(file + "\\"+jarName+".exe")).exists()) {
            if (startFrame.i) {
                System.out.println("当前路径:" + file);
                startFrame.i = false;
            }
        } else {
            try {
                bw = new BufferedWriter(new FileWriter(file + "\\debug.bat"));
//                bw.write("@echo off\n:: BatchGotAdmin (Run as Admin genshin_piano.code starts)\nREM --> Check for permissions\n>nul 2>&1 \"%SYSTEMROOT%\\system32\\cacls.exe\" \"%SYSTEMROOT%\\system32\\config\\system\"\nREM --> If error flag set, we do not have admin.\nif '%errorlevel%' NEQ '0' (\necho Requesting administrative privileges...\ngoto UACPrompt\n) else ( goto gotAdmin )\n:UACPrompt\necho Set UAC = CreateObject^(\"Shell.Application\"^) > \"%temp%\\getadmin.vbs\"\necho UAC.ShellExecute \"%~s0\", \"\", \"\", \"runas\", 1 >> \"%temp%\\getadmin.vbs\"\n\"%temp%\\getadmin.vbs\"\nexit /B\n:gotAdmin\nif exist \"%temp%\\getadmin.vbs\" ( del \"%temp%\\getadmin.vbs\" )\npushd \"%CD%\"\nCD /D \"%~dp0\"\n:: BatchGotAdmin (Run as Admin genshin_piano.code ends)\n\njava -Dfile.encoding=gbk -jar "+jarName+".jar\npause");
                bw.write("""
                        @echo off&color 3e
                        if exist "%SystemRoot%\\SysWOW64" path %path%;%windir%\\SysNative;%SystemRoot%\\SysWOW64;%~dp0
                        bcdedit >nul
                        if '%errorlevel%' NEQ '0' (goto UACPrompt) else (goto UACAdmin)
                        :UACPrompt
                        %1 start "" mshta vbscript:createobject("shell.application").shellexecute(""\"%~0""\","::",,"runas",1)(window.close)&exit
                        exit /B
                        :UACAdmin
                        cd /d "%~dp0"
                        java -Dfile.encoding=gbk -jar"""+" "+"GenshinPiano"+
                        """
                        .jar
                        pause""");
                bw.flush();
                bw.close();
                String fullPath = ExportResource("/"+jarName+".exe");
                String a = ExportResource("/song.zip");
                System.exit(0);
            } catch (IOException var3) {
                var3.printStackTrace();
                System.out.println("上面一坨的意思是生成start.bat失败，多半是路径问题\n\n请手动创建bat 内容为 java -jar genshin_piano.jar\n右键管理员运行，若不懂请百度“管理员运行jar”");
            } catch (Exception e) {
                e.printStackTrace();
            }
        }

    }
    /**
     * Export a resource embedded into a Jar file to the local file path.
     *
     * @param resourceName ie.: "/SmartLibrary.dll"
     * @return The path to the exported resource
     * @throws Exception
     */
    static public String ExportResource(String resourceName) throws Exception {
        InputStream stream = null;
        OutputStream resStreamOut = null;
        String jarFolder;
        try {
            stream = readMe.class.getResourceAsStream(resourceName);//note that each / is a directory down in the "jar tree" been the jar the root of the tree
            if(stream == null) {
                throw new Exception("Cannot get resource \"" + resourceName + "\" from Jar file.");
            }
            int readBytes;
            byte[] buffer = new byte[4096];
            jarFolder = new File(readMe.class.getProtectionDomain().getCodeSource().getLocation().toURI().getPath()).getParentFile().getPath().replace('\\', '/');
            resStreamOut = new FileOutputStream(jarFolder + resourceName);
            while ((readBytes = stream.read(buffer)) > 0) {
                resStreamOut.write(buffer, 0, readBytes);
            }
        } finally {
            assert stream != null;
            stream.close();
            assert resStreamOut != null;
            resStreamOut.close();
        }
        return jarFolder + resourceName;
    }
}
