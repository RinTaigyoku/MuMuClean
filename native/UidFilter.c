/* SPDX-License-Identifier: MIT
 * Short-lived loader for an exact socket-UID selector. No packet processing in userspace.
 * Kernel BPF interface: Linux UAPI. The loaded program uses the GPL-compatible helper API.
 */
#include <linux/bpf.h>
#include <sys/syscall.h>
#include <sys/resource.h>
#include <unistd.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>

static int call_bpf(int command, union bpf_attr *attr) {
    return syscall(SYS_bpf, command, attr, sizeof(*attr));
}
static struct bpf_insn instruction(unsigned char code, short off, int imm) {
    struct bpf_insn result={.code=code,.dst_reg=0,.src_reg=0,.off=off,.imm=imm};
    return result;
}
static int expected_program(int uid) {
    struct bpf_insn code[]={
        instruction(BPF_JMP|BPF_CALL,0,BPF_FUNC_get_socket_uid),
        instruction(BPF_JMP|BPF_JEQ|BPF_K,2,uid),
        instruction(BPF_ALU64|BPF_MOV|BPF_K,0,0),
        instruction(BPF_JMP|BPF_EXIT,0,0),
        instruction(BPF_ALU64|BPF_MOV|BPF_K,0,-1),
        instruction(BPF_JMP|BPF_EXIT,0,0)
    };
    char log[4096]={0}; union bpf_attr attr;memset(&attr,0,sizeof(attr));
    attr.prog_type=BPF_PROG_TYPE_SOCKET_FILTER;attr.insn_cnt=sizeof(code)/sizeof(code[0]);
    attr.insns=(unsigned long)code;attr.license=(unsigned long)"GPL";
    attr.log_level=1;attr.log_size=sizeof(log);attr.log_buf=(unsigned long)log;
    strcpy(attr.prog_name,"mumu_uid_v1");
    int fd=call_bpf(BPF_PROG_LOAD,&attr);
    if(fd<0)fprintf(stderr,"program load: %s\n%.512s\n",strerror(errno),log);
    return fd;
}
static int info(int fd,struct bpf_prog_info *result) {
    union bpf_attr attr;memset(&attr,0,sizeof(attr));memset(result,0,sizeof(*result));
    attr.info.bpf_fd=fd;attr.info.info_len=sizeof(*result);attr.info.info=(unsigned long)result;
    return call_bpf(BPF_OBJ_GET_INFO_BY_FD,&attr);
}
static int matches(int expected,int actual) {
    struct bpf_prog_info a,b;
    return info(expected,&a)==0 && info(actual,&b)==0 && a.type==b.type
        && memcmp(a.tag,b.tag,sizeof(a.tag))==0 && memcmp(a.name,b.name,sizeof(a.name))==0;
}
int main(int argc,char **argv) {
    if(argc!=3)return 2;
    if(strcmp(argv[1],"load") && strcmp(argv[1],"check") && strcmp(argv[1],"remove"))return 2;
    char *end=0;long uid=strtol(argv[2],&end,10);
    if(!argv[2][0] || *end || (uid!=1000 && (uid<10000 || uid>19999)))return 2;
    struct rlimit limit={RLIM_INFINITY,RLIM_INFINITY};
    if(setrlimit(RLIMIT_MEMLOCK,&limit)<0){perror("memlock");return 3;}
    int expected=expected_program(uid);if(expected<0)return 4;
    char path[96];snprintf(path,sizeof(path),"/sys/fs/bpf/mumuclean_uid_v1_%ld",uid);
    union bpf_attr attr;memset(&attr,0,sizeof(attr));attr.pathname=(unsigned long)path;
    int existing=call_bpf(BPF_OBJ_GET,&attr);int result=0;
    if(existing>=0) {
        if(!matches(expected,existing)){fprintf(stderr,"pin identity mismatch\n");result=5;}
        else if(!strcmp(argv[1],"remove") && unlink(path)<0){perror("unpin");result=6;}
        close(existing);
    }else if(errno!=ENOENT){perror("pin lookup");result=7;}
    else if(!strcmp(argv[1],"load")) {
        attr.bpf_fd=expected;
        if(call_bpf(BPF_OBJ_PIN,&attr)<0){perror("pin create");result=8;}
    }else if(!strcmp(argv[1],"check"))result=9;
    close(expected);
    if(!result)printf("MUMUCLEAN_UID_OK %ld\n",uid);
    return result;
}
